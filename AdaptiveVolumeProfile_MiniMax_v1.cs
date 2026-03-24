#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using System.Drawing;
#endregion

namespace NinjaTrader.Indicator
{
    /// <summary>
    /// AdaptiveVolumeProfile - Real-time session-based volume profile with SharpDX rendering.
    /// Displays volume distribution across price levels with POC, Value Area, and outside levels highlighted.
    /// </summary>
    public class AdaptiveVolumeProfile : Indicator
    {
        #region Variables
        private Dictionary<double, long> volumeProfile;
        private double pocPrice;
        private double valPrice;
        private double vahPrice;
        private long maxVolume;
        private long totalVolume;
        private bool sessionReset;
        private float rowHeight;

        // SharpDX brushes - class level for proper lifecycle management
        private SharpDX.Direct2D1.SolidColorBrush pocDxBrush;
        private SharpDX.Direct2D1.SolidColorBrush valueAreaDxBrush;
        private SharpDX.Direct2D1.SolidColorBrush outsideDxBrush;
        #endregion

        #region Properties

        [NinjaScriptProperty]
        [Display(Name = "Show Profile", Description = "Toggle visibility of the volume profile", Order = 1, GroupName = "Display")]
        public bool ShowProfile { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Number Of Levels", Description = "Number of price levels to display", Order = 2, GroupName = "Display")]
        public int NumberOfLevels { get; set; }

        [NinjaScriptProperty]
        [Range(50, 90)]
        [Display(Name = "Value Area %", Description = "Percentage of volume for Value Area", Order = 3, GroupName = "Display")]
        public int ValueAreaPercentage { get; set; }

        [NinjaScriptProperty]
        [Range(20, 300)]
        [Display(Name = "Max Bar Width", Description = "Maximum width of volume bars in pixels", Order = 4, GroupName = "Display")]
        public int MaxBarWidth { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Profile X Offset", Description = "Distance from right edge in pixels", Order = 5, GroupName = "Display")]
        public int ProfileXOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "POC Brush", Description = "Color for Point of Control", Order = 6, GroupName = "Colors")]
        public Brush POCBrush { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Value Area Brush", Description = "Color for Value Area levels", Order = 7, GroupName = "Colors")]
        public Brush ValueAreaBrush { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Outside Brush", Description = "Color for levels outside Value Area", Order = 8, GroupName = "Colors")]
        public Brush OutsideBrush { get; set; }

        #endregion

        #region State overrides

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AdaptiveVolumeProfile";
                Description = "Real-time session-based volume profile with POC and Value Area highlighting";

                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DrawOnPricePanel = true;
                DisplayInDataBox = false;
                DrawHorizontalGridLines = false;
                DrawVerticalGridLines = false;
                PaintPriceMarkers = false;
                ScaleJustification = ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                ShowProfile = true;
                NumberOfLevels = 25;
                ValueAreaPercentage = 70;
                MaxBarWidth = 150;
                ProfileXOffset = 10;

                POCBrush = Brushes.Yellow;
                ValueAreaBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 144, 255)); // DodgerBlue
                OutsideBrush = Brushes.Gray;
            }
            else if (State == State.Configure)
            {
                // No additional configuration needed
            }
            else if (State == State.DataLoaded)
            {
                volumeProfile = new Dictionary<double, long>();
                sessionReset = false;
                pocPrice = 0;
                valPrice = 0;
                vahPrice = 0;
                maxVolume = 0;
                totalVolume = 0;
            }
            else if (State == State.Terminated)
            {
                // Dispose SharpDX brushes
                if (pocDxBrush != null && !pocDxBrush.IsDisposed)
                {
                    pocDxBrush.Dispose();
                    pocDxBrush = null;
                }
                if (valueAreaDxBrush != null && !valueAreaDxBrush.IsDisposed)
                {
                    valueAreaDxBrush.Dispose();
                    valueAreaDxBrush = null;
                }
                if (outsideDxBrush != null && !outsideDxBrush.IsDisposed)
                {
                    outsideDxBrush.Dispose();
                    outsideDxBrush = null;
                }
            }
        }

        #endregion

        #region OnBarUpdate

        protected override void OnBarUpdate()
        {
            // Session detection - reset at start of new session
            if (Bars.IsFirstBarOfSession || sessionReset)
            {
                volumeProfile.Clear();
                sessionReset = false;
            }

            // Accumulate volume at normalized price level
            double normalizedPrice = Math.Round(Close[0] / TickSize) * TickSize;

            if (volumeProfile.ContainsKey(normalizedPrice))
                volumeProfile[normalizedPrice] += (long)Volume[0];
            else
                volumeProfile[normalizedPrice] = (long)Volume[0];

            // Recalculate POC and Value Area
            RecalculatePOCAndValueArea();
        }

        #endregion

        #region SharpDX Rendering

        protected override void OnRenderTargetChanged()
        {
            try
            {
                // Dispose existing brushes
                if (pocDxBrush != null && !pocDxBrush.IsDisposed)
                {
                    pocDxBrush.Dispose();
                    pocDxBrush = null;
                }
                if (valueAreaDxBrush != null && !valueAreaDxBrush.IsDisposed)
                {
                    valueAreaDxBrush.Dispose();
                    valueAreaDxBrush = null;
                }
                if (outsideDxBrush != null && !outsideDxBrush.IsDisposed)
                {
                    outsideDxBrush.Dispose();
                    outsideDxBrush = null;
                }

                // Create new brushes if RenderTarget is available
                if (RenderTarget != null)
                {
                    pocDxBrush = POCBrush.ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
                    valueAreaDxBrush = ValueAreaBrush.ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
                    outsideDxBrush = OutsideBrush.ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash
                Print("AdaptiveVolumeProfile: Error creating brushes - " + ex.Message);
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (!ShowProfile || volumeProfile == null || volumeProfile.Count == 0)
                return;

            if (pocDxBrush == null || valueAreaDxBrush == null || outsideDxBrush == null)
                return;

            try
            {
                // Calculate row height based on visible price range
                double visibleMinPrice = chartScale.MinValue;
                double visibleMaxPrice = chartScale.MaxValue;
                double visibleRange = visibleMaxPrice - visibleMinPrice;

                if (visibleRange <= 0 || NumberOfLevels <= 0)
                    return;

                rowHeight = (float)(visibleRange / NumberOfLevels);

                // Get visible bar range
                int fromIndex = ChartBars.FromIndex;
                int toIndex = ChartBars.ToIndex;

                if (fromIndex < 0 || toIndex < 0 || toIndex <= fromIndex)
                    return;

                // Calculate x position for right edge of profile
                float xRight = (float)(ChartPanel.X + ChartPanel.Width - ProfileXOffset);

                // Get all price levels sorted by price
                var sortedLevels = volumeProfile.OrderBy(kv => kv.Key).ToList();

                // Calculate max volume for scaling
                long localMaxVolume = sortedLevels.Max(kv => kv.Value);
                if (localMaxVolume <= 0)
                    return;

                // Draw volume bars for each price level
                foreach (var kvp in sortedLevels)
                {
                    // Calculate Y position (top of the bar)
                    float yTop = chartScale.GetYByValue(kvp.Key);

                    // Skip if outside visible area
                    if (yTop < ChartPanel.Y || yTop > ChartPanel.Y + ChartPanel.Height)
                        continue;

                    // Calculate bar width proportional to volume
                    float barWidth = (float)((kvp.Value / (double)localMaxVolume) * MaxBarWidth);
                    if (barWidth < 1)
                        barWidth = 1;

                    // Determine which brush to use
                    SharpDX.Direct2D1.SolidColorBrush brushToUse;
                    bool isPOC = Math.Abs(kvp.Key - pocPrice) < TickSize / 2.0;
                    bool isInValueArea = kvp.Key >= valPrice && kvp.Key <= vahPrice;

                    if (isPOC)
                        brushToUse = pocDxBrush;
                    else if (isInValueArea)
                        brushToUse = valueAreaDxBrush;
                    else
                        brushToUse = outsideDxBrush;

                    // Draw the filled rectangle
                    SharpDX.RectangleF rect = new SharpDX.RectangleF(
                        xRight - barWidth,
                        yTop - rowHeight / 2f,
                        barWidth,
                        rowHeight
                    );

                    RenderTarget.FillRectangle(rect, brushToUse);
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash - render failures should not stop the indicator
                Print("AdaptiveVolumeProfile: Render error - " + ex.Message);
            }
        }

        #endregion

        #region Calculations

        private void RecalculatePOCAndValueArea()
        {
            if (volumeProfile == null || volumeProfile.Count == 0)
                return;

            // Find POC (Point of Control) - price level with highest volume
            var maxKVP = volumeProfile.Aggregate((l, r) => l.Value > r.Value ? l : r);
            pocPrice = maxKVP.Key;
            maxVolume = maxKVP.Value;
            totalVolume = volumeProfile.Values.Sum();

            if (totalVolume <= 0)
                return;

            // Calculate target volume for Value Area
            long targetVolume = (long)(totalVolume * ValueAreaPercentage / 100.0);

            // Value Area calculation using proper volume-weighted expansion algorithm
            // Start from POC and expand toward the side with HIGHER volume first
            CalculateValueArea(targetVolume);
        }

        private void CalculateValueArea(long targetVolume)
        {
            if (volumeProfile.Count == 0)
                return;

            // Get all price levels sorted by distance from POC
            var sortedByDistance = volumeProfile
                .OrderBy(kv => Math.Abs(kv.Key - pocPrice))
                .ToList();

            // Separate levels above and below POC
            var abovePOC = sortedByDistance
                .Where(kv => kv.Key > pocPrice)
                .OrderBy(kv => kv.Key)
                .ToList();

            var belowPOC = sortedByDistance
                .Where(kv => kv.Key < pocPrice)
                .OrderByDescending(kv => kv.Key)
                .ToList();

            // Initialize Value Area with POC
            HashSet<double> valueAreaPrices = new HashSet<double> { pocPrice };
            long accumulatedVolume = maxVolume;
            long aboveVolume = abovePOC.Sum(kv => kv.Value);
            long belowVolume = belowPOC.Sum(kv => kv.Value);

            // Current expansion pointers
            int aboveIndex = 0;
            int belowIndex = 0;
            double currentAbovePrice = abovePOC.Count > 0 ? abovePOC[0].Key : double.MaxValue;
            double currentBelowPrice = belowPOC.Count > 0 ? belowPOC[0].Key : double.MinValue;

            // Expand toward the side with MORE volume first
            while (accumulatedVolume < targetVolume)
            {
                bool addAbove = false;
                bool addBelow = false;

                // Determine which side to expand from
                if (aboveIndex < abovePOC.Count && belowIndex < belowPOC.Count)
                {
                    // Both sides available - expand toward side with more volume
                    if (aboveVolume >= belowVolume)
                        addAbove = true;
                    else
                        addBelow = true;
                }
                else if (aboveIndex < abovePOC.Count)
                {
                    addAbove = true;
                }
                else if (belowIndex < belowPOC.Count)
                {
                    addBelow = true;
                }
                else
                {
                    // No more levels to add
                    break;
                }

                if (addAbove && aboveIndex < abovePOC.Count)
                {
                    valueAreaPrices.Add(currentAbovePrice);
                    accumulatedVolume += abovePOC[aboveIndex].Value;
                    aboveIndex++;
                    if (aboveIndex < abovePOC.Count)
                    {
                        currentAbovePrice = abovePOC[aboveIndex].Key;
                        aboveVolume = abovePOC.Skip(aboveIndex).Sum(kv => kv.Value);
                    }
                }
                else if (addBelow && belowIndex < belowPOC.Count)
                {
                    valueAreaPrices.Add(currentBelowPrice);
                    accumulatedVolume += belowPOC[belowIndex].Value;
                    belowIndex++;
                    if (belowIndex < belowPOC.Count)
                    {
                        currentBelowPrice = belowPOC[belowIndex].Key;
                        belowVolume = belowPOC.Skip(belowIndex).Sum(kv => kv.Value);
                    }
                }
            }

            // Set Value Area High and Low
            vahPrice = valueAreaPrices.Max();
            valPrice = valueAreaPrices.Min();
        }

        #endregion
    }
}
