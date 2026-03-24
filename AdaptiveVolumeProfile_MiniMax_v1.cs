#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DXGI;
using Factory = SharpDX.Direct2D1.Factory;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AdaptiveVolumeProfile : Indicator
    {
        #region Properties

        [NinjaScriptProperty]
        [Display(Name = "Show Profile", Description = "Show or hide the volume profile", GroupName = "General", Order = 1)]
        public bool ShowProfile
        {
            get { return showProfile; }
            set { showProfile = value; }
        }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Number of Levels", Description = "Number of price levels (rows) in the profile", GroupName = "General", Order = 2)]
        public int NumberOfLevels
        {
            get { return numberOfLevels; }
            set { numberOfLevels = Math.Max(5, Math.Min(100, value)); }
        }

        [NinjaScriptProperty]
        [Range(50, 90)]
        [Display(Name = "Value Area %", Description = "Percentage of volume for Value Area", GroupName = "General", Order = 3)]
        public int ValueAreaPercentage
        {
            get { return valueAreaPercentage; }
            set { valueAreaPercentage = Math.Max(50, Math.Min(90, value)); }
        }

        [NinjaScriptProperty]
        [Range(20, 300)]
        [Display(Name = "Max Bar Width", Description = "Maximum width of volume bars in pixels", GroupName = "General", Order = 4)]
        public int MaxBarWidth
        {
            get { return maxBarWidth; }
            set { maxBarWidth = Math.Max(20, Math.Min(300, value)); }
        }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "X Offset", Description = "Distance from right edge in pixels", GroupName = "General", Order = 5)]
        public int ProfileXOffset
        {
            get { return profileXOffset; }
            set { profileXOffset = Math.Max(5, Math.Min(100, value)); }
        }

        [XmlIgnore]
        [Display(Name = "POC Color", Description = "Color for Point of Control", GroupName = "Colors", Order = 10)]
        public System.Windows.Media.Brush POCBrush
        {
            get { return pocBrush; }
            set { pocBrush = value; }
        }

        [XmlIgnore]
        [Display(Name = "Value Area Color", Description = "Color for Value Area levels", GroupName = "Colors", Order = 11)]
        public System.Windows.Media.Brush ValueAreaBrush
        {
            get { return valueAreaBrush; }
            set { valueAreaBrush = value; }
        }

        [XmlIgnore]
        [Display(Name = "Outside Color", Description = "Color for levels outside Value Area", GroupName = "Colors", Order = 12)]
        public System.Windows.Media.Brush OutsideBrush
        {
            get { return outsideBrush; }
            set { outsideBrush = value; }
        }

        [Browsable(false)]
        public string POCBrushSerialize
        {
            get { return pocBrush != null ? pocBrush.ToString() : "Yellow"; }
            set { }
        }

        [Browsable(false)]
        public string ValueAreaBrushSerialize
        {
            get { return valueAreaBrush != null ? valueAreaBrush.ToString() : "DodgerBlue"; }
            set { }
        }

        [Browsable(false)]
        public string OutsideBrushSerialize
        {
            get { return outsideBrush != null ? outsideBrush.ToString() : "Gray"; }
            set { }
        }

        #endregion

        #region Fields

        private bool showProfile = true;
        private int numberOfLevels = 20;
        private int valueAreaPercentage = 70;
        private int maxBarWidth = 150;
        private int profileXOffset = 10;

        private System.Windows.Media.Brush pocBrush = System.Windows.Media.Brushes.Yellow;
        private System.Windows.Media.Brush valueAreaBrush = System.Windows.Media.Brushes.DodgerBlue;
        private System.Windows.Media.Brush outsideBrush = System.Windows.Media.Brushes.Gray;

        // Volume profile: price level -> volume at that level
        private Dictionary<double, double> volumeProfile;

        // Session tracking
        private bool sessionInitialized;
        private double lastBarVolume;
        private double totalSessionVolume;

        // POC and Value Area
        private double pocPrice;
        private double pocVolume;
        private HashSet<double> valueAreaPriceSet;

        // Price range for the session (for row-based bucketing)
        private double sessionLowPrice;
        private double sessionHighPrice;
        private double bucketSize;
        private bool priceRangeSet;

        // SharpDX brushes (class-level, disposed properly)
        private SharpDX.Direct2D1.SolidColorBrush pocDxBrush;
        private SharpDX.Direct2D1.SolidColorBrush valueAreaDxBrush;
        private SharpDX.Direct2D1.SolidColorBrush outsideDxBrush;

        #endregion

        #region State Management

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AdaptiveVolumeProfile";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DrawOnPricePanel = true;
                DisplayInDataBox = false;
                DrawHorizontalGridLines = false;
                DrawVerticalGridLines = false;
                PaintPriceMarkers = false;
            }
            else if (State == State.Configure)
            {
                // Initialize empty collections
                volumeProfile = new Dictionary<double, double>();
                valueAreaPriceSet = new HashSet<double>();

                // Reset session state
                sessionInitialized = false;
                lastBarVolume = 0;
                totalSessionVolume = 0;
                pocPrice = 0;
                pocVolume = 0;
                priceRangeSet = false;
            }
            else if (State == State.Terminated)
            {
                DisposeBrushes();
            }
        }

        #endregion

        #region Volume Accumulation

        protected override void OnBarUpdate()
        {
            // Only process primary series
            if (BarsInProgress != 0)
                return;

            // Skip first bar (no previous bar to calculate delta from)
            if (CurrentBar == 0)
                return;

            // Check for new session - reset once per session only
            if (Bars.IsFirstBarOfSession)
            {
                ResetSessionData();
            }

            // Skip if not initialized for this session yet
            if (!sessionInitialized)
                return;

            // Calculate incremental volume for this bar
            double currentBarTotalVolume = Volume[0];
            double incrementalVolume = currentBarTotalVolume - lastBarVolume;

            // Guard against invalid volume (shouldn't happen but be safe)
            if (incrementalVolume < 0)
                incrementalVolume = currentBarTotalVolume;

            // Only process if there's actual volume
            if (incrementalVolume > 0)
            {
                // Get price range of current bar for bucketing
                double barHigh = High[0];
                double barLow = Low[0];

                // Update session price range if needed
                if (!priceRangeSet || barHigh > sessionHighPrice)
                    sessionHighPrice = barHigh;
                if (!priceRangeSet || barLow < sessionLowPrice)
                    sessionLowPrice = barLow;

                // Ensure bucket boundaries are set
                if (!priceRangeSet)
                {
                    InitializeBuckets();
                    priceRangeSet = true;
                }

                // Distribute the incremental volume across price levels the bar traded through
                DistributeVolumeAcrossLevels(barLow, barHigh, incrementalVolume);

                // Update total session volume
                totalSessionVolume += incrementalVolume;

                // Recalculate POC and Value Area
                CalculatePOCAndValueArea();
            }

            // Track volume for next bar's delta calculation
            lastBarVolume = currentBarTotalVolume;
        }

        private void ResetSessionData()
        {
            // Only reset once per session - sessionInitialized prevents repeated clearing
            if (sessionInitialized)
            {
                volumeProfile.Clear();
                valueAreaPriceSet.Clear();
                totalSessionVolume = 0;
                pocPrice = 0;
                pocVolume = 0;
                priceRangeSet = false;
            }

            // Mark as initialized for this session
            sessionInitialized = true;
        }

        private void InitializeBuckets()
        {
            // Set up bucket boundaries based on session price range
            // Use the actual high/low of the session bars
            double tickSize = TickSize;

            // Round to nearest tick
            sessionLowPrice = Math.Round(sessionLowPrice / tickSize) * tickSize;
            sessionHighPrice = Math.Round(sessionHighPrice / tickSize) * tickSize;

            // Ensure we have a valid range
            if (sessionHighPrice <= sessionLowPrice)
            {
                sessionHighPrice = sessionLowPrice + tickSize;
            }

            // Calculate bucket size (price range / number of levels)
            bucketSize = (sessionHighPrice - sessionLowPrice) / NumberOfLevels;

            // Ensure bucket size is at least one tick
            if (bucketSize < tickSize)
                bucketSize = tickSize;
        }

        private void DistributeVolumeAcrossLevels(double barLow, double barHigh, double volume)
        {
            double tickSize = TickSize;

            // Normalize bar prices to tick boundaries
            double normalizedLow = Math.Round(barLow / tickSize) * tickSize;
            double normalizedHigh = Math.Round(barHigh / tickSize) * tickSize;

            // Ensure low <= high
            if (normalizedLow > normalizedHigh)
            {
                double temp = normalizedLow;
                normalizedLow = normalizedHigh;
                normalizedHigh = temp;
            }

            // Count how many price levels this bar spans
            double priceRange = normalizedHigh - normalizedLow;
            int levelsSpanned = Math.Max(1, (int)Math.Round(priceRange / tickSize) + 1);

            // Volume per tick/level
            double volumePerLevel = volume / levelsSpanned;

            // Add volume to each price level the bar traded through
            for (int i = 0; i < levelsSpanned; i++)
            {
                double priceLevel = normalizedLow + (i * tickSize);

                if (volumeProfile.ContainsKey(priceLevel))
                    volumeProfile[priceLevel] += volumePerLevel;
                else
                    volumeProfile[priceLevel] = volumePerLevel;
            }
        }

        private void CalculatePOCAndValueArea()
        {
            if (volumeProfile.Count == 0)
                return;

            // Find POC (price level with highest volume)
            var pocKvp = volumeProfile.Aggregate((l, r) => l.Value > r.Value ? l : r);
            pocPrice = pocKvp.Key;
            pocVolume = pocKvp.Value;

            // Target volume for Value Area
            double targetVolume = totalSessionVolume * (ValueAreaPercentage / 100.0);

            // Value Area expansion: start from POC, expand to adjacent level with larger volume
            valueAreaPriceSet.Clear();
            valueAreaPriceSet.Add(pocPrice);

            double accumulatedVolume = pocVolume;

            // Sort price levels above and below POC
            var pricesAbove = volumeProfile
                .Where(kv => kv.Key > pocPrice)
                .OrderBy(kv => kv.Key)
                .ToList();

            var pricesBelow = volumeProfile
                .Where(kv => kv.Key < pocPrice)
                .OrderByDescending(kv => kv.Key)
                .ToList();

            int aboveIndex = 0;
            int belowIndex = 0;

            // Compare FIRST adjacent level volumes (not total remaining)
            double aboveFirstVolume = aboveIndex < pricesAbove.Count ? pricesAbove[aboveIndex].Value : 0;
            double belowFirstVolume = belowIndex < pricesBelow.Count ? pricesBelow[belowIndex].Value : 0;

            while (accumulatedVolume < targetVolume)
            {
                bool addAbove = false;
                bool addBelow = false;

                // Compare the NEXT adjacent level on each side
                // NOT total remaining above vs below - this was the bug
                double nextAboveVol = aboveIndex < pricesAbove.Count ? pricesAbove[aboveIndex].Value : double.MaxValue;
                double nextBelowVol = belowIndex < pricesBelow.Count ? pricesBelow[belowIndex].Value : double.MaxValue;

                if (aboveIndex < pricesAbove.Count && belowIndex < pricesBelow.Count)
                {
                    // Both sides available - choose the one with larger volume at that level
                    addAbove = nextAboveVol >= nextBelowVol;
                    addBelow = !addAbove;
                }
                else if (aboveIndex < pricesAbove.Count)
                {
                    addAbove = true;
                }
                else if (belowIndex < pricesBelow.Count)
                {
                    addBelow = true;
                }
                else
                {
                    // No more levels to add
                    break;
                }

                // Add the selected level
                if (addAbove && aboveIndex < pricesAbove.Count)
                {
                    valueAreaPriceSet.Add(pricesAbove[aboveIndex].Key);
                    accumulatedVolume += pricesAbove[aboveIndex].Value;
                    aboveIndex++;
                }
                else if (addBelow && belowIndex < pricesBelow.Count)
                {
                    valueAreaPriceSet.Add(pricesBelow[belowIndex].Key);
                    accumulatedVolume += pricesBelow[belowIndex].Value;
                    belowIndex++;
                }
            }
        }

        #endregion

        #region SharpDX Rendering

        protected override void OnRenderTargetChanged()
        {
            DisposeBrushes();

            if (RenderTarget == null)
                return;

            try
            {
                pocDxBrush = new SharpDX.Direct2D1.SolidColorBrush(
                    RenderTarget,
                    POCBrush.ToDxColor4());

                valueAreaDxBrush = new SharpDX.Direct2D1.SolidColorBrush(
                    RenderTarget,
                    ValueAreaBrush.ToDxColor4());

                outsideDxBrush = new SharpDX.Direct2D1.SolidColorBrush(
                    RenderTarget,
                    OutsideBrush.ToDxColor4());
            }
            catch (Exception)
            {
                DisposeBrushes();
            }
        }

        private void DisposeBrushes()
        {
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

        protected override void OnRender(ChartControl chartControl, ChartPanels.ChartPanel chartPanel)
        {
            // Early exit if profile is hidden
            if (!ShowProfile)
                return;

            // Validate render targets
            if (chartControl == null || chartPanel == null || RenderTarget == null)
                return;

            // Ensure brushes are created
            if (pocDxBrush == null || valueAreaDxBrush == null || outsideDxBrush == null)
                return;

            // Need chart bars for price conversion
            ChartBars chartBars = ChartBars;
            if (chartBars == null)
                return;

            // Need volume data
            if (volumeProfile.Count == 0 || totalSessionVolume <= 0)
                return;

            try
            {
                // Get chart dimensions
                float panelWidth = chartPanel.Width;
                float panelHeight = chartPanel.Height;

                if (panelWidth <= 0 || panelHeight <= 0)
                    return;

                // Right edge of the profile area
                float profileRightX = panelWidth - ProfileXOffset;

                // Find max volume for scaling
                double maxVolume = volumeProfile.Values.Max();
                if (maxVolume <= 0)
                    return;

                // Calculate visible price range from chart
                // Use BarsArray to get price range
                double maxPrice = Closes[0][BarsArray[0].ToIndex];
                double minPrice = Closes[0][BarsArray[0].ToIndex];

                for (int i = BarsArray[0].FromIndex; i <= BarsArray[0].ToIndex; i++)
                {
                    if (i >= 0 && i < Closes[0].Length)
                    {
                        double high = Highs[0][i];
                        double low = Lows[0][i];
                        if (high > maxPrice) maxPrice = high;
                        if (low < minPrice) minPrice = low;
                    }
                }

                double priceRange = maxPrice - minPrice;
                if (priceRange <= 0)
                    return;

                // Calculate pixels per price
                float pixelsPerPrice = panelHeight / (float)priceRange;

                // Row height for each price level (equal distribution across visible range)
                float rowHeight = panelHeight / (float)NumberOfLevels;

                // Draw each price level
                foreach (var kvp in volumeProfile)
                {
                    double priceLevel = kvp.Key;
                    double levelVolume = kvp.Value;

                    // Skip prices outside visible range (with small margin)
                    if (priceLevel < minPrice - TickSize || priceLevel > maxPrice + TickSize)
                        continue;

                    // Calculate Y position (inverted - price increases going up)
                    float yCenter = panelHeight - (float)((priceLevel - minPrice) / priceRange * panelHeight);

                    // Calculate bar width proportional to volume vs POC
                    float barWidth = (float)((levelVolume / maxVolume) * MaxBarWidth);
                    if (barWidth < 1)
                        barWidth = 1;

                    // Calculate bar X position (anchored to right edge)
                    float barLeft = profileRightX - barWidth;

                    // Determine brush color
                    SharpDX.Direct2D1.SolidColorBrush brush;

                    bool isPOC = Math.Abs(priceLevel - pocPrice) < TickSize / 2.0;
                    bool isInValueArea = valueAreaPriceSet.Contains(priceLevel);

                    if (isPOC)
                        brush = pocDxBrush;
                    else if (isInValueArea)
                        brush = valueAreaDxBrush;
                    else
                        brush = outsideDxBrush;

                    // Draw the volume bar
                    RectangleF rect = new RectangleF(
                        barLeft,
                        yCenter - rowHeight / 2f,
                        barWidth,
                        rowHeight);

                    RenderTarget.FillRectangle(rect, brush);
                }
            }
            catch (Exception)
            {
                // Silently handle render errors to avoid flooding the log
            }
        }

        #endregion
    }
}

#region NinjaScript Builder Code
public class AdaptiveVolumeProfile : Indicator
{
    // This section is for NinjaScript Builder reference only
    // DO NOT MODIFY
}
#endregion
