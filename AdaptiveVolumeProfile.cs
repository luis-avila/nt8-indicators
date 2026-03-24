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
        private Dictionary<double, long> volumeProfile;           // TickSize-normalized price level -> Volume
        private double pocPrice;                                   // Price level with highest volume
        private double vah;                                        // Value Area High
        private double val;                                        // Value Area Low
        private long maxVolume;                                    // Volume at POC
        private DateTime previousSessionBegin;                     // Track last session start
        private bool needsRecalculation;                          // Flag to recalculate POC and Value Area
        private SharpDX.Direct2D1.SolidColorBrush pocBrushDx;     // Brush for POC
        private SharpDX.Direct2D1.SolidColorBrush valueAreaBrushDx; // Brush for Value Area
        private SharpDX.Direct2D1.SolidColorBrush outsideBrushDx;   // Brush for other levels
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
                
                // Parameter defaults
                ShowProfile                 = true;
                NumberOfRows                = 50;
                ProfileXOffset              = 30;
                MaxBarWidth                 = 150;
                ValueAreaPercentage         = 70;
                Opacity                     = 70;
                
                // Default colors
                POCBrush                    = Brushes.Gold;
                ValueAreaBrush              = Brushes.DodgerBlue;
                OutsideBrush                = Brushes.SlateGray;
            }
            else if (State == State.Configure)
            {
                // Add session iterator for session tracking
                SessionIterator sessionIterator = new SessionIterator(Bars);
            }
            else if (State == State.DataLoaded)
            {
                // Initialize data structures
                volumeProfile = new Dictionary<double, long>();
                previousSessionBegin = Bars.Session.GetSessionBegin(Bars, 0);
                needsRecalculation = true;
            }
            else if (State == State.Terminated)
            {
                // Cleanup and dispose brushes
                if (pocBrushDx != null && !pocBrushDx.IsDisposed)
                    pocBrushDx.Dispose();
                if (valueAreaBrushDx != null && !valueAreaBrushDx.IsDisposed)
                    valueAreaBrushDx.Dispose();
                if (outsideBrushDx != null && !outsideBrushDx.IsDisposed)
                    outsideBrushDx.Dispose();
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

            // Accumulate volume based on trade data
            if (e.MarketDataType == MarketDataType.Last)
            {
                // Normalize price to TickSize for stable fixed buckets
                double tickSize = Instrument.MasterInstrument.TickSize;
                double normalizedPrice = Math.Round(e.Price / tickSize) * tickSize;
                
                // Accumulate volume at the tickSize-normalized price level
                if (volumeProfile.ContainsKey(normalizedPrice))
                    volumeProfile[normalizedPrice] += e.Volume;
                else
                    volumeProfile.Add(normalizedPrice, e.Volume);
                
                needsRecalculation = true;
            }
        }

        private void ResetSessionData()
        {
            volumeProfile.Clear();
            pocPrice = 0;
            vah = 0;
            val = 0;
            maxVolume = 0;
            needsRecalculation = true;
        }

        private void RecalculatePOCAndValueArea()
        {
            if (volumeProfile.Count == 0)
                return;
            
            // Find POC (price level with highest volume)
            var maxKV = volumeProfile.Aggregate((l, r) => l.Value > r.Value ? l : r);
            pocPrice = maxKV.Key;
            maxVolume = maxKV.Value;
            
            // Calculate total volume
            long totalVolume = volumeProfile.Values.Sum();
            long targetVol = (long)(totalVolume * ValueAreaPercentage / 100.0);
            long accumulatedVol = maxVolume;
            
            // Create list of price levels sorted by volume (descending)
            var sortedByVolume = volumeProfile.OrderByDescending(kv => kv.Value).ToList();
            
            // Find POC index in sorted list
            int pocIndex = sortedByVolume.FindIndex(kv => kv.Key == pocPrice);
            if (pocIndex < 0)
                return;
            
            // Initialize Value Area with just POC
            vah = pocPrice;
            val = pocPrice;
            
            // Expand Value Area using price-level expansion algorithm
            // Compare volume at next price level above vs below current boundaries
            int leftIndex = pocIndex - 1;
            int rightIndex = pocIndex + 1;
            
            while (accumulatedVol < targetVol && (leftIndex >= 0 || rightIndex < sortedByVolume.Count))
            {
                double leftVolume = leftIndex >= 0 ? sortedByVolume[leftIndex].Value : 0;
                double rightVolume = rightIndex < sortedByVolume.Count ? sortedByVolume[rightIndex].Value : 0;
                
                if (leftVolume >= rightVolume && leftIndex >= 0)
                {
                    // Expand to the left (lower prices)
                    accumulatedVol += leftVolume;
                    val = Math.Min(val, sortedByVolume[leftIndex].Key);
                    leftIndex--;
                }
                else if (rightIndex < sortedByVolume.Count)
                {
                    // Expand to the right (higher prices)
                    accumulatedVol += rightVolume;
                    vah = Math.Max(vah, sortedByVolume[rightIndex].Key);
                    rightIndex++;
                }
                else if (leftIndex >= 0)
                {
                    // Only left available
                    accumulatedVol += leftVolume;
                    val = Math.Min(val, sortedByVolume[leftIndex].Key);
                    leftIndex--;
                }
            }
            
            needsRecalculation = false;
        }

        public override void OnRenderTargetChanged()
        {
            // Create or recreate brushes when RenderTarget changes
            if (pocBrushDx != null && !pocBrushDx.IsDisposed)
                pocBrushDx.Dispose();
            pocBrushDx = POCBrush.ToDxBrush(RenderTarget);
            
            if (valueAreaBrushDx != null && !valueAreaBrushDx.IsDisposed)
                valueAreaBrushDx.Dispose();
            valueAreaBrushDx = ValueAreaBrush.ToDxBrush(RenderTarget);
            
            if (outsideBrushDx != null && !outsideBrushDx.IsDisposed)
                outsideBrushDx.Dispose();
            outsideBrushDx = OutsideBrush.ToDxBrush(RenderTarget);
            
            // Set opacity for all brushes
            float opacity = (float)(Opacity / 100.0);
            if (pocBrushDx != null) pocBrushDx.Opacity = opacity;
            if (valueAreaBrushDx != null) valueAreaBrushDx.Opacity = opacity;
            if (outsideBrushDx != null) outsideBrushDx.Opacity = opacity;
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            // Check if we should render
            if (!ShowProfile || volumeProfile.Count == 0 || IsInHitTest)
                return;
            
            // Recalculate POC and Value Area if needed
            if (needsRecalculation)
                RecalculatePOCAndValueArea();
            
            // Get chart dimensions
            float rightX = ChartPanel.X + ChartPanel.Width;
            float chartHeight = ChartPanel.Height;
            
            // Skip if chart dimensions are invalid
            if (rightX <= 0 || chartHeight <= 0)
                return;
            
            // Get price range from chart scale
            double minPrice = chartScale.MinValue;
            double maxPrice = chartScale.MaxValue;
            double priceRange = maxPrice - minPrice;
            
            if (priceRange <= 0 || maxVolume <= 0)
                return;
            
            // Calculate row height based on number of rows and chart height
            float rowHeight = Math.Max(3, chartHeight / NumberOfRows);
            
            // Get visible bar range
            int fromIndex = ChartBars.FromIndex;
            int toIndex = ChartBars.ToIndex;
            
            // Draw horizontal volume bars for each price level in profile
            foreach (var kvp in volumeProfile)
            {
                double price = kvp.Key;
                long volume = kvp.Value;
                
                // Skip if outside visible price range
                if (price < minPrice || price > maxPrice)
                    continue;
                
                // Calculate bar width proportional to volume relative to POC
                float barWidth = (float)((volume / (double)maxVolume) * MaxBarWidth);
                barWidth = Math.Max(1, Math.Min(barWidth, MaxBarWidth));
                
                // Get Y position for this price level
                float yPosition = chartScale.GetYByValue(price);
                float yTop = yPosition - rowHeight / 2;
                
                // Skip if bar is completely outside chart
                if (yTop > chartHeight || yTop + rowHeight < 0)
                    continue;
                
                // Determine brush based on price level
                SharpDX.Direct2D1.SolidColorBrush brush;
                
                // Check if this is the POC level (within TickSize/2)
                if (Math.Abs(price - pocPrice) <= Instrument.MasterInstrument.TickSize / 2)
                    brush = pocBrushDx;
                // Check if within Value Area
                else if (price >= val && price <= vah)
                    brush = valueAreaBrushDx;
                else
                    brush = outsideBrushDx;
                
                // Create rectangle for the bar
                float xPosition = rightX - ProfileXOffset - barWidth;
                float barHeight = Math.Max(1, rowHeight);
                
                // Draw the volume bar
                SharpDX.RectangleF rect = new SharpDX.RectangleF(
                    xPosition,    // x
                    yTop,         // y
                    barWidth,     // width
                    barHeight     // height
                );
                
                RenderTarget.FillRectangle(rect, brush);
            }
            
            // Draw Value Area outline (thin vertical line at left edge of VA)
            if (vah > val && vah != pocPrice)
            {
                float vaTop = chartScale.GetYByValue(vah);
                float vaBottom = chartScale.GetYByValue(val);
                float vaHeight = Math.Max(1, Math.Abs(vaBottom - vaTop));
                
                float outlineX = rightX - ProfileXOffset - MaxBarWidth - 2;
                
                using (var outlineBrush = new SharpDX.Direct2D1.SolidColorBrush(
                    RenderTarget, new SharpDX.Color4(1f, 1f, 1f, 0.7f)))
                {
                    SharpDX.RectangleF vaRect = new SharpDX.RectangleF(
                        outlineX,
                        Math.Min(vaTop, vaBottom),
                        2,
                        vaHeight
                    );
                    RenderTarget.FillRectangle(vaRect, outlineBrush);
                }
            }
            
            // Draw POC horizontal line marker
            if (pocPrice > 0)
            {
                float pocY = chartScale.GetYByValue(pocPrice);
                
                using (var pocLineBrush = new SharpDX.Direct2D1.SolidColorBrush(
                    RenderTarget, new SharpDX.Color4(1f, 1f, 0f, 0.9f)))
                {
                    RenderTarget.DrawLine(
                        new SharpDX.Vector2(rightX - ProfileXOffset - MaxBarWidth - 5, pocY),
                        new SharpDX.Vector2(rightX - ProfileXOffset, pocY),
                        pocLineBrush, 2f);
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
        [Range(10, 200)]
        [Display(Name = "Profile X Offset", Description = "Distance from right edge in pixels", Order = 3, GroupName = "Visual")]
        public int ProfileXOffset { get; set; }
        
        [NinjaScriptProperty]
        [Range(10, 500)]
        [Display(Name = "Max Bar Width", Description = "Maximum bar width in pixels (POC width)", Order = 4, GroupName = "Visual")]
        public int MaxBarWidth { get; set; }
        
        [NinjaScriptProperty]
        [Range(10, 90)]
        [Display(Name = "Value Area %", Description = "Percentage of total volume for Value Area", Order = 5, GroupName = "Parameters")]
        public int ValueAreaPercentage { get; set; }
        
        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Opacity", Description = "Opacity of volume bars (10-100)", Order = 6, GroupName = "Visual")]
        public int Opacity { get; set; }
        
        [XmlIgnore]
        [Display(Name = "POC Brush", Description = "Brush for Point of Control level", Order = 7, GroupName = "Colors")]
        public Brush POCBrush { get; set; }
        
        [Browsable(false)]
        public string POCBrushSerialize
        {
            get { return Serialize.BrushToString(POCBrush); }
            set { POCBrush = Serialize.StringToBrush(value); }
        }
        
        [XmlIgnore]
        [Display(Name = "Value Area Brush", Description = "Brush for Value Area bars", Order = 8, GroupName = "Colors")]
        public Brush ValueAreaBrush { get; set; }
        
        [Browsable(false)]
        public string ValueAreaBrushSerialize
        {
            get { return Serialize.BrushToString(ValueAreaBrush); }
            set { ValueAreaBrush = Serialize.StringToBrush(value); }
        }
        
        [XmlIgnore]
        [Display(Name = "Outside Brush", Description = "Brush for bars outside Value Area", Order = 9, GroupName = "Colors")]
        public Brush OutsideBrush { get; set; }
        
        [Browsable(false)]
        public string OutsideBrushSerialize
        {
            get { return Serialize.BrushToString(OutsideBrush); }
            set { OutsideBrush = Serialize.StringToBrush(value); }
        }
        
        #endregion
    }
}
