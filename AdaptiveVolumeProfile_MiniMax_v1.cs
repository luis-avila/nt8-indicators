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
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using SharpDX.Mathematics.Interop;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// AdaptiveVolumeProfile - Real-time volume profile indicator for NinjaTrader 8
    /// Displays volume distribution across price levels with POC, Value Area highlighting
    /// </summary>
    public class AdaptiveVolumeProfile : Indicator
    {
        #region Properties

        [NinjaScriptProperty]
        [Range(10, 200)]
        [Display(Name = "Number Of Levels", Description = "Number of price levels to display", Order = 1, GroupName = "Parameters")]
        public int NumberOfLevels { get; set; }

        [NinjaScriptProperty]
        [Range(50, 99)]
        [Display(Name = "Value Area %", Description = "Percentage of total volume for Value Area", Order = 2, GroupName = "Parameters")]
        public double ValueAreaPercentage { get; set; }

        [NinjaScriptProperty]
        [Range(20, 200)]
        [Display(Name = "Max Bar Width", Description = "Maximum width of volume bars in pixels", Order = 3, GroupName = "Parameters")]
        public float MaxBarWidth { get; set; }

        [NinjaScriptProperty]
        [Range(5, 50)]
        [Display(Name = "Profile X Offset", Description = "Distance from right edge in pixels", Order = 4, GroupName = "Parameters")]
        public float ProfileXOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Profile", Description = "Show or hide the volume profile", Order = 5, GroupName = "Parameters")]
        public bool ShowProfile { get; set; }

        [XmlIgnore]
        [Display(Name = "POC Color", Description = "Color for Point of Control", Order = 1, GroupName = "Colors")]
        public Brush POCBrush { get; set; }

        [Browsable(false)]
        public string POCBrushSerialize
        {
            get { return Serialize.BrushToString(POCBrush); }
            set { POCBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Value Area Color", Description = "Color for Value Area levels", Order = 2, GroupName = "Colors")]
        public Brush ValueAreaBrush { get; set; }

        [Browsable(false)]
        public string ValueAreaBrushSerialize
        {
            get { return Serialize.BrushToString(ValueAreaBrush); }
            set { ValueAreaBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Outside VA Color", Description = "Color for levels outside Value Area", Order = 3, GroupName = "Colors")]
        public Brush OutsideBrush { get; set; }

        [Browsable(false)]
        public string OutsideBrushSerialize
        {
            get { return Serialize.BrushToString(OutsideBrush); }
            set { OutsideBrush = Serialize.StringToBrush(value); }
        }

        #endregion

        #region Private variables

        private Dictionary<double, long> volumeProfile;
        private SessionIterator sessionIterator;
        private DateTime previousSessionBegin;

        // POC and Value Area values
        private double pocPrice;
        private double vahPrice;
        private double valPrice;
        private long maxVolume;
        private long totalSessionVolume;

        // SharpDX brushes - created in OnRenderTargetChanged, disposed in State.Terminated
        private SharpDX.Direct2D1.SolidColorBrush pocBrushDx;
        private SharpDX.Direct2D1.SolidColorBrush valueAreaBrushDx;
        private SharpDX.Direct2D1.SolidColorBrush outsideBrushDx;

        // Flag to track if profile needs recalculation
        private bool profileNeedsUpdate;

        #endregion

        #region State Management

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AdaptiveVolumeProfile";
                Description = "Real-time volume profile with POC and Value Area highlighting";

                // Calculate settings - use OnEachTick for real-time volume updates
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = false;
                DrawVerticalGridLines = false;
                PaintPriceMarkers = false;
                ScaleJustification = ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                // Default property values
                NumberOfLevels = 50;
                ValueAreaPercentage = 70.0;
                MaxBarWidth = 100.0f;
                ProfileXOffset = 10.0f;
                ShowProfile = true;

                // Default brush colors
                POCBrush = Brushes.Yellow;
                ValueAreaBrush = Brushes.DodgerBlue;
                OutsideBrush = Brushes.Gray;

                profileNeedsUpdate = false;
            }
            else if (State == State.DataLoaded)
            {
                // Initialize SessionIterator and volume profile dictionary
                sessionIterator = new SessionIterator(Bars);
                volumeProfile = new Dictionary<double, long>();

                // Get the initial session begin time
                if (sessionIterator != null)
                {
                    previousSessionBegin = sessionIterator.ActualSessionBegin;
                }

                // Reset profile state
                pocPrice = 0;
                vahPrice = 0;
                valPrice = 0;
                maxVolume = 0;
                totalSessionVolume = 0;
            }
            else if (State == State.Terminated)
            {
                // Dispose SharpDX brushes to prevent memory leaks
                if (pocBrushDx != null && !pocBrushDx.IsDisposed)
                {
                    pocBrushDx.Dispose();
                }
                if (valueAreaBrushDx != null && !valueAreaBrushDx.IsDisposed)
                {
                    valueAreaBrushDx.Dispose();
                }
                if (outsideBrushDx != null && !outsideBrushDx.IsDisposed)
                {
                    outsideBrushDx.Dispose();
                }
            }
        }

        #endregion

        #region Session Detection and Volume Accumulation

        protected override void OnBarUpdate()
        {
            // Check for new session - use IsFirstBarOfSession instead of Time[0]
            if (Bars.IsFirstBarOfSession)
            {
                // Clear volume profile for new session
                volumeProfile.Clear();

                // Update session iterator
                if (sessionIterator != null)
                {
                    previousSessionBegin = sessionIterator.ActualSessionBegin;
                }

                // Reset profile values
                pocPrice = 0;
                vahPrice = 0;
                valPrice = 0;
                maxVolume = 0;
                totalSessionVolume = 0;
                profileNeedsUpdate = true;
            }

            // Only accumulate if we have valid data
            if (CurrentBar < 0 || Bars == null || Bars.Instrument == null)
                return;

            // Normalize price to TickSize for stable bucket identification
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

            // Mark profile for recalculation
            profileNeedsUpdate = true;
        }

        #endregion

        #region POC and Value Area Calculation

        private void RecalculatePOCAndVA()
        {
            if (volumeProfile == null || volumeProfile.Count == 0)
                return;

            // Find POC - the price level with highest volume
            var maxKVP = volumeProfile.Aggregate((l, r) => l.Value > r.Value ? l : r);
            pocPrice = maxKVP.Key;
            maxVolume = maxKVP.Value;

            // Calculate total volume in profile
            totalSessionVolume = volumeProfile.Values.Sum();

            if (totalSessionVolume == 0)
                return;

            // Calculate target volume for Value Area
            long targetVolume = (long)(totalSessionVolume * ValueAreaPercentage / 100.0);

            // Use price-level expansion algorithm from POC outward
            // Start from POC and expand toward prices with more cumulative volume
            List<double> valueAreaPrices = new List<double> { pocPrice };
            long accumulatedVolume = maxVolume;

            // Get all prices sorted by distance from POC
            var sortedByDistance = volumeProfile
                .Where(kv => Math.Abs(kv.Key - pocPrice) > TickSize / 2.0) // Exclude POC itself
                .OrderBy(kv => Math.Abs(kv.Key - pocPrice))
                .ThenBy(kv => kv.Key)
                .ToList();

            foreach (var kv in sortedByDistance)
            {
                if (accumulatedVolume >= targetVolume)
                    break;

                valueAreaPrices.Add(kv.Key);
                accumulatedVolume += kv.Value;
            }

            // Set VAH and VAL
            if (valueAreaPrices.Count > 0)
            {
                vahPrice = valueAreaPrices.Max();
                valPrice = valueAreaPrices.Min();
            }

            profileNeedsUpdate = false;
        }

        #endregion

        #region SharpDX Brush Management

        protected override void OnRenderTargetChanged()
        {
            // Dispose old brushes if they exist
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

            // Create new brushes with current RenderTarget
            if (RenderTarget != null)
            {
                try
                {
                    pocBrushDx = POCBrush.ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
                    valueAreaBrushDx = ValueAreaBrush.ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
                    outsideBrushDx = OutsideBrush.ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
                }
                catch (Exception)
                {
                    // Handle brush creation failure gracefully
                    pocBrushDx = null;
                    valueAreaBrushDx = null;
                    outsideBrushDx = null;
                }
            }
        }

        #endregion

        #region Rendering

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            // Early exit conditions
            if (!ShowProfile)
                return;

            if (chartControl == null || chartScale == null || ChartPanel == null)
                return;

            if (RenderTarget == null)
                return;

            if (volumeProfile == null || volumeProfile.Count == 0)
                return;

            // Recalculate POC and VA if needed
            if (profileNeedsUpdate)
            {
                RecalculatePOCAndVA();
            }

            if (maxVolume == 0)
                return;

            // Calculate X position for the profile on the right side of chart
            float xRight = ChartPanel.X + ChartPanel.Width - ProfileXOffset;

            // Get visible bar range for efficiency
            int fromIndex = ChartBars.FromIndex;
            int toIndex = ChartBars.ToIndex;

            // Calculate approximate row height based on visible range
            double visibleHigh = chartScale.MaxValue;
            double visibleLow = chartScale.MinValue;
            double visibleRange = visibleHigh - visibleLow;

            if (visibleRange <= 0)
                return;

            // Sort price levels for drawing
            var sortedLevels = volumeProfile.OrderBy(kv => kv.Key).ToList();

            // Draw each price level as a horizontal bar
            foreach (var kvp in sortedLevels)
            {
                double priceLevel = kvp.Key;
                long volume = kvp.Value;

                // Get Y position for this price level
                float yPos = chartScale.GetYByValue(priceLevel);

                // Calculate bar width proportional to volume relative to POC
                float barWidth = (float)(volume / (double)maxVolume) * MaxBarWidth;

                // Ensure minimum visibility
                if (barWidth < 1.0f)
                    barWidth = 1.0f;

                // Select appropriate brush based on price level
                SharpDX.Direct2D1.SolidColorBrush brushToUse = null;

                // Check if this is POC (within half a tick)
                if (Math.Abs(priceLevel - pocPrice) < TickSize / 2.0)
                {
                    brushToUse = pocBrushDx;
                }
                // Check if within Value Area
                else if (priceLevel >= valPrice && priceLevel <= vahPrice)
                {
                    brushToUse = valueAreaBrushDx;
                }
                else
                {
                    brushToUse = outsideBrushDx;
                }

                // Skip if no valid brush
                if (brushToUse == null)
                    continue;

                // Calculate rectangle for the volume bar
                // Bar extends left from xRight
                float left = xRight - barWidth;
                float top = yPos - (float)(visibleRange / (toIndex - fromIndex + 1)) / 2.0f;
                float bottom = yPos + (float)(visibleRange / (toIndex - fromIndex + 1)) / 2.0f;

                // Ensure minimum height
                if (bottom - top < 1.0f)
                    bottom = top + 1.0f;

                SharpDX.RectangleF rect = new SharpDX.RectangleF(left, top, barWidth, bottom - top);

                // Draw the rectangle using pre-created brush
                RenderTarget.FillRectangle(rect, brushToUse);
            }
        }

        #endregion

        #region Data Series Access

        // This indicator operates on the primary data series
        // No additional data series needed

        #endregion
    }
}
