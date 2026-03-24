// ============================================================================
// AdaptiveVolumeProfile - NinjaTrader 8 Custom Indicator
// ============================================================================
// Real-time volume profile for current session with SharpDX rendering
// Features:
// - Session-based volume profile (resets automatically at session start)
// - Point of Control (POC) highlighting
// - Value Area (configurable %) highlighting
// - Proportional horizontal bar widths
// - All SharpDX rendering with proper brush management
// ============================================================================

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using SharpDX.Direct2D1;
using SharpDX.Mathematics.Interop;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AdaptiveVolumeProfile : Indicator
    {
        #region Variables
        private Dictionary<double, long> volumeProfile;
        private SessionIterator sessionIterator;
        private DateTime previousSessionBegin;
        
        // Calculated values
        private double pocPrice;
        private double vahPrice;
        private double valPrice;
        private long maxVolume;
        private long totalSessionVolume;
        private HashSet<double> valueAreaPrices;
        
        // SharpDX brushes - created in OnRenderTargetChanged, disposed in State.Terminated
        private SharpDX.Direct2D1.Brush pocBrushDx;
        private SharpDX.Direct2D1.Brush valueAreaBrushDx;
        private SharpDX.Direct2D1.Brush outsideBrushDx;
        
        // Flag to track if we need to recalculate POC/VA
        private bool needsRecalculation;
        #endregion

        #region State Management
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                // Indicator properties
                Name = "AdaptiveVolumeProfile";
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = false;
                DrawVerticalGridLines = false;
                PaintPriceMarkers = false;
                ScaleJustification = ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
                
                // Calculate setting - OnEachTick for real-time volume updates
                Calculate = Calculate.OnEachTick;
                
                // Default property values
                ShowProfile = true;
                NumberOfLevels = 25;
                ValueAreaPercentage = 70.0;
                MaxBarWidth = 150;
                ProfileXOffset = 30;
                
                // Default brush colors
                POCBrush = Brushes.Yellow;
                ValueAreaBrush = Brushes.DodgerBlue;
                OutsideBrush = Brushes.Gray;
                
                needsRecalculation = false;
            }
            else if (State == State.DataLoaded)
            {
                // Initialize data structures
                sessionIterator = new SessionIterator(Bars);
                volumeProfile = new Dictionary<double, long>();
                valueAreaPrices = new HashSet<double>();
                previousSessionBegin = sessionIterator.ActualSessionBegin;
                
                // Initialize brushes to null - will be created in OnRenderTargetChanged
                pocBrushDx = null;
                valueAreaBrushDx = null;
                outsideBrushDx = null;
            }
            else if (State == State.Terminated)
            {
                // Dispose SharpDX brushes when indicator is removed
                if (pocBrushDx != null && !pocBrushDx.IsDisposed)
                {
                    pocBrushDx.Dispose();
                    pocBrushDx = null;
                }
                if (valueAreaBrushDx != null && !valueAreaBrushDx.IsDisposed)
                {
                    valueAreaBrushDx.Dispose();
                    valueAreaBrushDx = null;
                }
                if (outsideBrushDx != null && !outsideBrushDx.IsDisposed)
                {
                    outsideBrushDx.Dispose();
                    outsideBrushDx = null;
                }
            }
        }
        #endregion

        #region Bar Update Logic
        protected override void OnBarUpdate()
        {
            // Skip if not enough bars
            if (CurrentBar < 0)
                return;
            
            // Check for new session using Bars property
            if (Bars.IsFirstBarOfSession)
            {
                // New session detected - reset volume profile
                volumeProfile.Clear();
                valueAreaPrices.Clear();
                needsRecalculation = true;
            }
            
            // Normalize current close price to TickSize for stable bucketing
            double normalizedPrice = Math.Round(Close[0] / TickSize) * TickSize;
            
            // Accumulate volume at this price level
            if (volumeProfile.ContainsKey(normalizedPrice))
            {
                volumeProfile[normalizedPrice] += (long)Volume[0];
            }
            else
            {
                volumeProfile[normalizedPrice] = (long)Volume[0];
            }
            
            // Mark for recalculation
            needsRecalculation = true;
        }
        #endregion

        #region SharpDX Brush Management
        protected override void OnRenderTargetChanged()
        {
            // Dispose existing brushes before creating new ones
            if (pocBrushDx != null && !pocBrushDx.IsDisposed)
            {
                pocBrushDx.Dispose();
                pocBrushDx = null;
            }
            if (valueAreaBrushDx != null && !valueAreaBrushDx.IsDisposed)
            {
                valueAreaBrushDx.Dispose();
                valueAreaBrushDx = null;
            }
            if (outsideBrushDx != null && !outsideBrushDx.IsDisposed)
            {
                outsideBrushDx.Dispose();
                outsideBrushBrushDx = null;
            }
            
            // Create new SharpDX brushes if RenderTarget is available
            if (RenderTarget != null)
            {
                try
                {
                    pocBrushDx = POCBrush.ToDxBrush(RenderTarget);
                    valueAreaBrushDx = ValueAreaBrush.ToDxBrush(RenderTarget);
                    outsideBrushDx = OutsideBrush.ToDxBrush(RenderTarget);
                }
                catch (Exception ex)
                {
                    // Log error but don't crash - indicator will render without custom colors
                    Log("AdaptiveVolumeProfile: Error creating SharpDX brushes - " + ex.Message, NinjaTrader.Core.LogLevel.Error);
                }
            }
        }
        #endregion

        #region Rendering
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            // Skip rendering if profile is hidden or no data
            if (!ShowProfile || volumeProfile == null || volumeProfile.Count == 0)
                return;
            
            // Skip if brushes not initialized
            if (pocBrushDx == null || valueAreaBrushDx == null || outsideBrushDx == null)
                return;
            
            // Recalculate POC and Value Area if needed
            if (needsRecalculation)
            {
                RecalculatePOCAndValueArea();
                needsRecalculation = false;
            }
            
            // Safety check
            if (maxVolume <= 0)
                return;
            
            try
            {
                // Calculate right edge position
                float xRight = (float)(ChartPanel.X + ChartPanel.Width - ProfileXOffset);
                
                // Sort price levels for rendering
                var sortedLevels = volumeProfile.OrderBy(kv => kv.Key).ToList();
                
                // Calculate row height based on number of visible levels
                double minPrice = sortedLevels.First().Key;
                double maxPrice = sortedLevels.Last().Key;
                double priceRange = maxPrice - minPrice;
                
                if (priceRange <= 0)
                    return;
                
                // Determine how many levels we can display based on chart height
                int displayLevels = Math.Min(sortedLevels.Count, NumberOfLevels);
                float rowHeight = (float)(ChartPanel.Height / (double)displayLevels);
                
                // Group prices into buckets if needed
                double bucketSize = priceRange / NumberOfLevels;
                Dictionary<double, long> bucketedProfile = new Dictionary<double, long>();
                
                foreach (var kv in sortedLevels)
                {
                    double bucketKey = Math.Round((kv.Key - minPrice) / bucketSize) * bucketSize + minPrice;
                    bucketKey = Math.Round(bucketKey / TickSize) * TickSize; // Normalize to TickSize
                    
                    if (bucketedProfile.ContainsKey(bucketKey))
                        bucketedProfile[bucketKey] += kv.Value;
                    else
                        bucketedProfile[bucketKey] = kv.Value;
                }
                
                // Render each bucket
                foreach (var kv in bucketedProfile.OrderBy(kv => kv.Key))
                {
                    // Get Y position for this price level
                    float yTop = chartScale.GetYByValue(kv.Key);
                    
                    // Calculate bar width proportional to volume relative to POC
                    float barWidth = (float)((kv.Value / (double)maxVolume) * MaxBarWidth);
                    
                    // Determine which brush to use based on price level
                    SharpDX.Direct2D1.Brush brushToUse;
                    
                    // Check if this is the POC level (within half a tick)
                    if (Math.Abs(kv.Key - pocPrice) < TickSize / 2)
                    {
                        brushToUse = pocBrushDx;
                    }
                    // Check if this level is within Value Area
                    else if (valueAreaPrices.Contains(Math.Round(kv.Key / TickSize) * TickSize))
                    {
                        brushToUse = valueAreaBrushDx;
                    }
                    else
                    {
                        brushToUse = outsideBrushDx;
                    }
                    
                    // Draw the horizontal bar
                    // Rectangle: x, y, width, height
                    // xRight - barWidth gives us the left edge (bars grow from right)
                    // yTop - rowHeight/2 positions the bar centered on the price level
                    var rect = new SharpDX.RectangleF(
                        xRight - barWidth,
                        yTop - rowHeight / 2,
                        barWidth,
                        rowHeight
                    );
                    
                    RenderTarget.FillRectangle(rect, brushToUse);
                }
            }
            catch (Exception ex)
            {
                // Log any rendering errors but don't crash
                Log("AdaptiveVolumeProfile: Render error - " + ex.Message, NinjaTrader.Core.LogLevel.Error);
            }
        }
        #endregion

        #region POC and Value Area Calculation
        private void RecalculatePOCAndValueArea()
        {
            if (volumeProfile == null || volumeProfile.Count == 0)
                return;
            
            // Find POC - the price level with highest volume
            var maxPair = volumeProfile.Aggregate((l, r) => l.Value > r.Value ? l : r);
            pocPrice = maxPair.Key;
            maxVolume = maxPair.Value;
            
            // Calculate total session volume
            totalSessionVolume = volumeProfile.Values.Sum();
            
            // Calculate target volume for Value Area
            long targetVolume = (long)(totalSessionVolume * ValueAreaPercentage / 100.0);
            
            // Clear and rebuild Value Area using price-level expansion algorithm
            valueAreaPrices.Clear();
            valueAreaPrices.Add(pocPrice);
            
            long accumulatedVolume = maxVolume;
            
            // Get sorted price levels
            var sortedPrices = volumeProfile.Keys.OrderBy(p => p).ToList();
            int pocIndex = sortedPrices.IndexOf(pocPrice);
            
            // Expand from POC outward toward the side with more volume
            // Compare volume above vs below at each expansion step
            double currentVal = pocPrice;
            double currentVah = pocPrice;
            
            while (accumulatedVolume < targetVolume && (currentVal > sortedPrices.First() || currentVah < sortedPrices.Last()))
            {
                // Calculate volume above and below current boundaries
                long volumeAbove = 0;
                long volumeBelow = 0;
                
                double nextAbovePrice = double.MaxValue;
                double nextBelowPrice = double.MinValue;
                
                foreach (var kv in volumeProfile)
                {
                    if (kv.Key > currentVah && kv.Key < nextAbovePrice)
                    {
                        nextAbovePrice = kv.Key;
                        volumeAbove = kv.Value;
                    }
                    if (kv.Key < currentVal && kv.Key > nextBelowPrice)
                    {
                        nextBelowPrice = kv.Key;
                        volumeBelow = kv.Value;
                    }
                }
                
                // Expand toward the side with MORE volume
                if (volumeAbove >= volumeBelow && nextAbovePrice < double.MaxValue)
                {
                    currentVah = nextAbovePrice;
                    accumulatedVolume += volumeAbove;
                    valueAreaPrices.Add(nextAbovePrice);
                }
                else if (nextBelowPrice > double.MinValue)
                {
                    currentVal = nextBelowPrice;
                    accumulatedVolume += volumeBelow;
                    valueAreaPrices.Add(nextBelowPrice);
                }
                else
                {
                    // Fallback: expand whichever side is available
                    if (nextAbovePrice < double.MaxValue)
                    {
                        currentVah = nextAbovePrice;
                        accumulatedVolume += volumeAbove;
                        valueAreaPrices.Add(nextAbovePrice);
                    }
                    if (nextBelowPrice > double.MinValue)
                    {
                        currentVal = nextBelowPrice;
                        accumulatedVolume += volumeBelow;
                        valueAreaPrices.Add(nextBelowPrice);
                    }
                }
            }
            
            // Set final VA boundaries
            vahPrice = currentVah;
            valPrice = currentVal;
        }
        #endregion

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Show Profile", Description = "Show or hide the volume profile", GroupName = "General", Order = 1)]
        public bool ShowProfile
        { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Number of Levels", Description = "Number of price levels to display", GroupName = "General", Order = 2)]
        public int NumberOfLevels
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 99)]
        [Display(Name = "Value Area %", Description = "Percentage of volume for Value Area", GroupName = "General", Order = 3)]
        public double ValueAreaPercentage
        { get; set; }

        [NinjaScriptProperty]
        [Range(50, 300)]
        [Display(Name = "Max Bar Width", Description = "Maximum width of volume profile bars in pixels", GroupName = "Display", Order = 4)]
        public int MaxBarWidth
        { get; set; }

        [NinjaScriptProperty]
        [Range(10, 200)]
        [Display(Name = "Profile X Offset", Description = "Distance from right edge of chart in pixels", GroupName = "Display", Order = 5)]
        public int ProfileXOffset
        { get; set; }

        [XmlIgnore]
        [Display(Name = "POC Color", Description = "Color for Point of Control", GroupName = "Colors", Order = 6)]
        public Brush POCBrush
        { get; set; }

        [Browsable(false)]
        public string POCBrushSerialize
        {
            get { return Serialize.BrushToString(POCBrush); }
            set { POCBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Value Area Color", Description = "Color for Value Area levels", GroupName = "Colors", Order = 7)]
        public Brush ValueAreaBrush
        { get; set; }

        [Browsable(false)]
        public string ValueAreaBrushSerialize
        {
            get { return Serialize.BrushToString(ValueAreaBrush); }
            set { ValueAreaBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Outside VA Color", Description = "Color for levels outside Value Area", GroupName = "Colors", Order = 8)]
        public Brush OutsideBrush
        { get; set; }

        [Browsable(false)]
        public string OutsideBrushSerialize
        {
            get { return Serialize.BrushToString(OutsideBrush); }
            set { OutsideBrush = Serialize.StringToBrush(value); }
        }

        // Output plots for programmatic access
        [Browsable(false)]
        [XmlIgnore]
        public double POC
        {
            get { return pocPrice; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public double VAH
        {
            get { return vahPrice; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public double VAL
        {
            get { return valPrice; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public long MaxVolumeLevel
        {
            get { return maxVolume; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public long TotalVolume
        {
            get { return totalSessionVolume; }
        }
        #endregion
    }
}
