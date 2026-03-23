#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// This namespace holds indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// AdaptiveVolumeProfile - Calculates and displays a real-time volume profile for the current session
    /// as horizontal bars on the right side of the price panel. Shows volume distribution across price levels,
    /// highlights the Point of Control (POC) and Value Area (70% of volume centered around POC).
    /// </summary>
    public class AdaptiveVolumeProfile : Indicator
    {
        #region Private Variables
        private Dictionary<double, double> volumeByPrice;  // Price level -> Volume
        private double sessionTotalVolume;
        private double pocPrice;
        private double pocVolume;
        private double valueAreaLow;
        private double valueAreaHigh;
        private DateTime currentSessionDate;
        private bool needsRecalculation;
        private double lastAskPrice;
        private double lastBidPrice;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                 = @"Calculates a real-time volume profile for the current session only. Displays as horizontal bars on the right side showing volume distribution across price levels. Highlights POC and Value Area (70% of total volume centered around POC).";
                Name                        = "AdaptiveVolumeProfile";
                Calculate                   = Calculate.OnEachTick;
                IsOverlay                   = true;
                DisplayInDataBox            = false;
                DrawOnPricePanel            = true;
                DrawHorizontalGridLines     = true;
                DrawVerticalGridLines       = true;
                PaintPriceMarkers           = false;
                IsSuspendedWhileInactive    = true;
                IsChartOnly                 = true;
                
                // Parameter defaults
                ShowProfile                 = true;
                NumberOfRows                = 50;
                BarWidthPercentage          = 0.3;
                ValueAreaPercentage         = 70.0;
                Opacity                     = 70;
                
                // Default colors
                POCColor                    = Brushes.Gold;
                ValueAreaColor              = Brushes.DodgerBlue;
                OutsideColor                = Brushes.SlateGray;
            }
            else if (State == State.Configure)
            {
                // Set ZOrder to negative to draw behind price bars
                ZOrder = -1;
            }
            else if (State == State.DataLoaded)
            {
                // Initialize data structures
                volumeByPrice =new Dictionary<double, double>();
                ResetSessionData();
            }
            else if (State == State.Terminated)
            {
                // Cleanup
                if (volumeByPrice != null)
                    volumeByPrice.Clear();
            }
        }

        protected override void OnBarUpdate()
        {
            // Reset session data at start of new session
            if (Bars.IsFirstBarOfSession)
            {
                ResetSessionData();
            }
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            // Only process if we should show the profile
            if (!ShowProfile)
                return;

            // Handle tick replay mode (has bid/ask data)
            if (Bars.IsTickReplay)
            {
                if (e.MarketDataType == MarketDataType.Ask)
                {
                    lastAskPrice = e.Price;
                    return;
                }
                
                if (e.MarketDataType == MarketDataType.Bid)
                {
                    lastBidPrice = e.Price;
                    return;
                }
                
                if (e.MarketDataType == MarketDataType.Last)
                {
                    if (lastAskPrice == 0 || lastBidPrice == 0)
                        return;
                        
                    // Accumulate volume at the traded price
                    AccumulateVolume(e.Price, e.Volume);
                    needsRecalculation = true;
                }
            }
            else
            {
                // Non-tick replay mode - accumulate based on last price
                if (e.MarketDataType == MarketDataType.Last)
                {
                    AccumulateVolume(e.Price, e.Volume);
                    needsRecalculation = true;
                }
            }
        }

        private void ResetSessionData()
        {
            volumeByPrice.Clear();
            sessionTotalVolume = 0;
            pocPrice = 0;
            pocVolume = 0;
            valueAreaLow = 0;
            valueAreaHigh = 0;
            currentSessionDate = Time[0].Date;
            lastAskPrice = 0;
            lastBidPrice = 0;
            needsRecalculation = true;
        }

        private void AccumulateVolume(double price, long volume)
        {
            // Normalize price to tick size
            double tickSize = Instrument.MasterInstrument.Ticksize;
            double normalizedPrice = Math.Round(price / tickSize) * tickSize;
            
            if (volumeByPrice.ContainsKey(normalizedPrice))
                volumeByPrice[normalizedPrice] += volume;
            else
                volumeByPrice.Add(normalizedPrice, volume);
            
            sessionTotalVolume += volume;
        }

        private void CalculatePOCAndValueArea()
        {
            if (volumeByPrice.Count == 0)
                return;
            
            // Find POC (price with highest volume)
            pocVolume = 0;
            pocPrice = 0;
            foreach (var kvp in volumeByPrice)
            {
                if (kvp.Value > pocVolume)
                {
                    pocVolume = kvp.Value;
                    pocPrice = kvp.Key;
                }
            }
            
            // Calculate Value Area (70% of total volume centered around POC)
            if (volumeByPrice.Count > 0)
            {
                double targetVolume = sessionTotalVolume * (ValueAreaPercentage / 100.0);
                double accumulatedVolume = 0;
                
                // Sort prices by volume descending
                var sortedByVolume = volumeByPrice.OrderByDescending(kvp => kvp.Value).ToList();
                
                // Start with POC
                accumulatedVolume = pocVolume;
                valueAreaLow = pocPrice;
                valueAreaHigh = pocPrice;
                
                // Expand outward until we have 70% of volume
                int pocIndex = sortedByVolume.FindIndex(kvp => kvp.Key == pocPrice);
                int leftIndex = pocIndex - 1;
                int rightIndex = pocIndex + 1;
                
                while (accumulatedVolume < targetVolume && (leftIndex >= 0 || rightIndex < sortedByVolume.Count))
                {
                    double leftVolume = leftIndex >= 0 ? sortedByVolume[leftIndex].Value : 0;
                    double rightVolume = rightIndex < sortedByVolume.Count ? sortedByVolume[rightIndex].Value : 0;
                    
                    if (leftVolume >= rightVolume && leftIndex >= 0)
                    {
                        // Expand to the left
                        accumulatedVolume += leftVolume;
                        valueAreaLow = Math.Min(valueAreaLow, sortedByVolume[leftIndex].Key);
                        leftIndex--;
                    }
                    else if (rightIndex < sortedByVolume.Count)
                    {
                        // Expand to the right
                        accumulatedVolume += rightVolume;
                        valueAreaHigh = Math.Max(valueAreaHigh, sortedByVolume[rightIndex].Key);
                        rightIndex++;
                    }
                    else if (leftIndex >= 0)
                    {
                        // Only left available
                        accumulatedVolume += leftVolume;
                        valueAreaLow = Math.Min(valueAreaLow, sortedByVolume[leftIndex].Key);
                        leftIndex--;
                    }
                }
            }
            
            needsRecalculation = false;
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            
            // Check if we should render
            if (!ShowProfile || volumeByPrice == null || volumeByPrice.Count == 0 || IsInHitTest)
                return;
            
            // Recalculate POC and Value Area if needed
            if (needsRecalculation)
                CalculatePOCAndValueArea();
            
            // Get visible price range
            double minPrice = chartScale.MinValue;
            double maxPrice = chartScale.MaxValue;
            double priceRange = maxPrice - minPrice;
            
            if (priceRange <= 0)
                return;
            
            // Calculate price step based on number of rows
            double tickSize = Instrument.MasterInstrument.TickSize;
            double priceStep = Math.Max(tickSize, priceRange / NumberOfRows);
            
            // Calculate bar width
            float maxBarWidth = (float)(ChartPanel.W * BarWidthPercentage);
            if (maxBarWidth <= 0)
                return;
            
            // Create brushes with proper disposal
            using (var pocBrush = POCColor.ToDxBrush(RenderTarget))
            using (var valueAreaBrush = ValueAreaColor.ToDxBrush(RenderTarget))
            using (var outsideBrush = OutsideColor.ToDxBrush(RenderTarget))
            {
                // Set opacity
                float opacity = (float)(Opacity / 100.0);
                pocBrush.Opacity = opacity;
                valueAreaBrush.Opacity = opacity;
                outsideBrush.Opacity = opacity;
                
                // Get right side X position
                float rightX = ChartPanel.X + ChartPanel.W;
                
                // Set antialiasing to aliased for better performance with many bars
                var savedMode = RenderTarget.AntialiasMode;
                RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;
                
                try
                {
                    // Iterate through price levels
                    for (int i = 0; i < NumberOfRows; i++)
                    {
                        double priceLevel = maxPrice - (i * priceStep);
                        double nextPriceLevel = priceLevel - priceStep;
                        
                        // Skip if outside visible range
                        if (priceLevel < minPrice || nextPriceLevel > maxPrice)
                            continue;
                            
                        // Calculate volume in this price range
                        double volumeInRange = 0;
                        foreach (var kvp in volumeByPrice)
                        {
                            if (kvp.Key <= priceLevel && kvp.Key > nextPriceLevel)
                                volumeInRange += kvp.Value;
                        }
                        
                        if (volumeInRange <= 0)
                            continue;
                        
                        // Calculate bar width proportional to volume
                        float barWidth = pocVolume > 0 ? (float)((volumeInRange / pocVolume) * maxBarWidth) : 0;
                        if (barWidth < 1)
                            barWidth = 1;
                        
                        // Determine color based on POC and Value Area
                        SharpDX.Direct2D1.Brush brush;
                        if (Math.Abs(priceLevel - pocPrice) < tickSize / 2)
                        {
                            // POC level
                            brush = pocBrush;
                        }
                        else if (priceLevel >= valueAreaLow && priceLevel <= valueAreaHigh)
                        {
                            // Within Value Area
                            brush = valueAreaBrush;
                        }
                        else
                        {
                            // Outside Value Area
                            brush = outsideBrush;
                        }
                        
                        // Calculate Y positions for this price range
                        float yTop = chartScale.GetYByValue(priceLevel);
                        float yBottom = chartScale.GetYByValue(nextPriceLevel);
                        float height = Math.Max(1, Math.Abs(yBottom - yTop));
                        
                        // Create rectangle for the bar
                        SharpDX.RectangleF barRect = new SharpDX.RectangleF(
                            rightX - barWidth,    // x
                            yTop,                 // y
                            barWidth,             // width
                            height                // height
                        );
                        
                        // Draw the bar
                        RenderTarget.FillRectangle(barRect, brush);
                    }
                    
                    // Draw outline for Value Area
                    if (valueAreaHigh > valueAreaLow)
                    {
                        float vaTop = chartScale.GetYByValue(valueAreaHigh);
                        float vaBottom = chartScale.GetYByValue(valueAreaLow);
                        float vaHeight = Math.Abs(vaBottom - vaTop);
                        
                        SharpDX.RectangleF vaRect = new SharpDX.RectangleF(
                            rightX - maxBarWidth - 2,
                            vaTop,
                            2,
                            vaHeight
                        );
                        
                        using (var outlineBrush = new SharpDX.Direct2D1.SolidColorBrush(
                            RenderTarget, new SharpDX.Color4(1f, 1f, 1f, 0.8f)))
                        {
                            RenderTarget.FillRectangle(vaRect, outlineBrush);
                        }
                    }
                    
                    // Draw POC line
                    float pocY = chartScale.GetYByValue(pocPrice);
                    using (var pocLineBrush = new SharpDX.Direct2D1.SolidColorBrush(
                        RenderTarget, new SharpDX.Color4(1f, 1f, 0f, 0.9f)))
                    {
                        RenderTarget.DrawLine(
                            new SharpDX.Vector2(rightX - maxBarWidth - 5, pocY),
                            new SharpDX.Vector2(rightX, pocY),
                            pocLineBrush, 2f);
                    }
                }
                finally
                {
                    // Restore antialiasing mode
                    RenderTarget.AntialiasMode = savedMode;
                }
            }
        }

        #region Properties
        
        [NinjaScriptProperty]
        [Display(Name = "Show Profile", Description = "Toggle visibility of the volume profile", Order = 1, GroupName = "Visual")]
        public bool ShowProfile { get; set; }
        
        [NinjaScriptProperty]
        [Range(10, 200)]
        [Display(Name = "Number of Rows", Description = "Number of price levels to display", Order = 2, GroupName = "Visual")]
        public int NumberOfRows { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Bar Width %", Description = "Maximum bar width as percentage of panel width", Order = 3, GroupName = "Visual")]
        public double BarWidthPercentage { get; set; }
        
        [NinjaScriptProperty]
        [Range(10, 90)]
        [Display(Name = "Value Area %", Description = "Percentage of total volume for Value Area", Order = 4, GroupName = "Parameters")]
        public double ValueAreaPercentage { get; set; }
        
        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Opacity", Description = "Opacity of volume bars (10-100)", Order = 5, GroupName = "Visual")]
        public int Opacity { get; set; }
        
        [XmlIgnore]
        [Display(Name = "POC Color", Description = "Color for Point of Control", Order = 6, GroupName = "Colors")]
        public Brush POCColor { get; set; }
        
        [Browsable(false)]
        public string POCColorSerialize
        {
            get { return Serialize.BrushToString(POCColor); }
            set { POCColor = Serialize.StringToBrush(value); }
        }
        
        [XmlIgnore]
        [Display(Name = "Value Area Color", Description = "Color for Value Area bars", Order = 7, GroupName = "Colors")]
        public Brush ValueAreaColor { get; set; }
        
        [Browsable(false)]
        public string ValueAreaColorSerialize
        {
            get { return Serialize.BrushToString(ValueAreaColor); }
            set { ValueAreaColor = Serialize.StringToBrush(value); }
        }
        
        [XmlIgnore]
        [Display(Name = "Outside Color", Description = "Color for bars outside Value Area", Order = 8, GroupName = "Colors")]
        public Brush OutsideColor { get; set; }
        
        [Browsable(false)]
        public string OutsideColorSerialize
        {
            get { return Serialize.BrushToString(OutsideColor); }
            set { OutsideColor = Serialize.StringToBrush(value); }
        }
        
        #endregion
    }
}