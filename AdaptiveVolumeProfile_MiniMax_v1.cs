namespace NinjaTrader.NinjaScript.Indicators
{
    public class AdaptiveVolumeProfile : Indicator
    {
        private Dictionary<double, long> volumeProfile;
        private double pocPrice;
        private double vahPrice;
        private double valPrice;
        private long maxVolume;
        private bool sessionReset;

        // SharpDX brushes - created in OnRenderTargetChanged
        private SharpDX.Direct2D1.SolidColorBrush pocDxBrush;
        private SharpDX.Direct2D1.SolidColorBrush valueAreaDxBrush;
        private SharpDX.Direct2D1.SolidColorBrush outsideDxBrush;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AdaptiveVolumeProfile";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = false;
                DrawHorizontalGridLines = false;
                DrawVerticalGridLines = false;
                PaintPriceMarkers = false;
                ScaleJustification = ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                ShowProfile = true;
                NumberOfLevels = 25;
                ValueAreaPercentage = 70;
                MaxBarWidth = 150;
                ProfileXOffset = 20;
                POCBrush = System.Windows.Media.Brushes.Yellow;
                ValueAreaBrush = System.Windows.Media.Brushes.DodgerBlue;
                OutsideBrush = System.Windows.Media.Brushes.Gray;
            }
            else if (State == State.DataLoaded)
            {
                volumeProfile = new Dictionary<double, long>();
            }
            else if (State == State.Terminated)
            {
                // Dispose SharpDX brushes
                if (pocDxBrush != null && !pocDxBrush.IsDisposed)
                    pocDxBrush.Dispose();
                if (valueAreaDxBrush != null && !valueAreaDxBrush.IsDisposed)
                    valueAreaDxBrush.Dispose();
                if (outsideDxBrush != null && !outsideDxBrush.IsDisposed)
                    outsideDxBrush.Dispose();
            }
        }

        protected override void OnRenderTargetChanged()
        {
            try
            {
                // Dispose existing brushes before creating new ones
                if (pocDxBrush != null && !pocDxBrush.IsDisposed)
                    pocDxBrush.Dispose();
                if (valueAreaDxBrush != null && !valueAreaDxBrush.IsDisposed)
                    valueAreaDxBrush.Dispose();
                if (outsideDxBrush != null && !outsideDxBrush.IsDisposed)
                    outsideDxBrush.Dispose();

                // Create new SharpDX brushes from WPF brushes
                if (RenderTarget != null)
                {
                    pocDxBrush = POCBrush.ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
                    valueAreaDxBrush = ValueAreaBrush.ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
                    outsideDxBrush = OutsideBrush.ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
                }
            }
            catch
            {
                // Swallow exceptions during brush creation to prevent crashes
            }
        }

        protected override void OnBarUpdate()
        {
            // Check for new session and reset profile
            if (Bars.IsFirstBarOfSession || sessionReset)
            {
                volumeProfile.Clear();
                sessionReset = false;
            }

            // Skip if not enough bars
            if (CurrentBar < 1)
                return;

            // Normalize price to TickSize for stable bucketing
            double normalizedPrice = Math.Round(Close[0] / TickSize) * TickSize;

            // Accumulate volume at this price level
            if (volumeProfile.ContainsKey(normalizedPrice))
                volumeProfile[normalizedPrice] += (long)Volume[0];
            else
                volumeProfile[normalizedPrice] = (long)Volume[0];

            // Recalculate POC and Value Area
            RecalculatePOCAndValueArea();
        }

        private void RecalculatePOCAndValueArea()
        {
            if (volumeProfile.Count == 0)
                return;

            // Find POC (price level with highest volume)
            maxVolume = 0;
            pocPrice = 0;
            foreach (var kvp in volumeProfile)
            {
                if (kvp.Value > maxVolume)
                {
                    maxVolume = kvp.Value;
                    pocPrice = kvp.Key;
                }
            }

            if (maxVolume == 0)
                return;

            // Calculate total volume
            long totalVolume = 0;
            foreach (var vol in volumeProfile.Values)
                totalVolume += vol;

            // Calculate target volume for Value Area
            long targetVolume = (long)(totalVolume * ValueAreaPercentage / 100.0);

            // Value Area calculation: expand from POC toward prices with MORE volume first
            // Sort all price levels by distance from POC, then by volume (higher volume first)
            var sortedByDistance = volumeProfile
                .OrderBy(kv => Math.Abs(kv.Key - pocPrice))
                .ThenByDescending(kv => kv.Value)
                .ToList();

            List<double> valueAreaPrices = new List<double>();
            valueAreaPrices.Add(pocPrice);
            long accumulatedVolume = maxVolume;

            // Expand toward prices with higher cumulative volume
            while (accumulatedVolume < targetVolume && valueAreaPrices.Count < volumeProfile.Count)
            {
                bool expanded = false;

                // Try to expand above POC
                double abovePrice = valueAreaPrices.Max() + TickSize;
                if (volumeProfile.ContainsKey(abovePrice))
                {
                    long volAbove = volumeProfile[abovePrice];
                    long volBelow = 0;
                    double belowPrice = valueAreaPrices.Min() - TickSize;
                    if (volumeProfile.ContainsKey(belowPrice))
                        volBelow = volumeProfile[belowPrice];

                    // Expand toward the side with MORE volume
                    if (volAbove >= volBelow && !valueAreaPrices.Contains(abovePrice))
                    {
                        valueAreaPrices.Add(abovePrice);
                        accumulatedVolume += volAbove;
                        expanded = true;
                    }
                    else if (!valueAreaPrices.Contains(belowPrice))
                    {
                        valueAreaPrices.Add(belowPrice);
                        accumulatedVolume += volBelow;
                        expanded = true;
                    }
                }
                else if (!valueAreaPrices.Contains(belowPrice))
                {
                    valueAreaPrices.Add(belowPrice);
                    accumulatedVolume += volBelow;
                    expanded = true;
                }

                if (!expanded)
                {
                    // Add next nearest price level
                    foreach (var kv in sortedByDistance)
                    {
                        if (!valueAreaPrices.Contains(kv.Key))
                        {
                            valueAreaPrices.Add(kv.Key);
                            accumulatedVolume += kv.Value;
                            expanded = true;
                            break;
                        }
                    }
                }

                if (!expanded)
                    break;
            }

            vahPrice = valueAreaPrices.Max();
            valPrice = valueAreaPrices.Min();
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            // Early exit if profile is hidden or no data
            if (!ShowProfile || volumeProfile == null || volumeProfile.Count == 0)
                return;

            // Ensure brushes are initialized
            if (pocDxBrush == null || valueAreaDxBrush == null || outsideDxBrush == null)
                return;

            try
            {
                // Calculate right edge position
                float xRight = (float)(ChartPanel.X + ChartPanel.Width - ProfileXOffset);

                // Get sorted price levels from low to high
                var sortedLevels = volumeProfile.OrderBy(kv => kv.Key).ToList();

                // Calculate row height based on price range
                double minPrice = sortedLevels.First().Key;
                double maxPrice = sortedLevels.Last().Key;
                double priceRange = maxPrice - minPrice;

                // Calculate how many rows we have and spacing
                float totalHeight = (float)((maxPrice - minPrice) / TickSize) * chartScale.GetYByValue(minPrice + TickSize) - chartScale.GetYByValue(minPrice);
                float rowHeight = Math.Abs(totalHeight / sortedLevels.Count);
                rowHeight = Math.Max(rowHeight, 2); // Minimum row height of 2 pixels

                // Render each price level
                for (int i = 0; i < sortedLevels.Count; i++)
                {
                    var kvp = sortedLevels[i];

                    // Calculate Y position
                    float yTop = chartScale.GetYByValue(kvp.Key);

                    // Calculate bar width proportional to volume
                    float barWidth = (float)((kvp.Value / (double)maxVolume) * MaxBarWidth);
                    barWidth = Math.Max(barWidth, 1); // Minimum bar width of 1 pixel

                    // Select appropriate brush
                    SharpDX.Direct2D1.SolidColorBrush brush;
                    double priceLevel = kvp.Key;

                    // Check if this price level is the POC
                    bool isPOC = Math.Abs(priceLevel - pocPrice) < TickSize / 2.0;
                    bool isInValueArea = priceLevel >= valPrice && priceLevel <= vahPrice;

                    if (isPOC)
                        brush = pocDxBrush;
                    else if (isInValueArea)
                        brush = valueAreaDxBrush;
                    else
                        brush = outsideDxBrush;

                    // Draw the horizontal bar
                    SharpDX.RectangleF rect = new SharpDX.RectangleF(
                        xRight - barWidth,
                        yTop - rowHeight / 2f,
                        barWidth,
                        rowHeight
                    );

                    RenderTarget.FillRectangle(rect, brush);
                }
            }
            catch
            {
                // Swallow render exceptions to prevent indicator crashes
            }
        }

        #region Properties

        [NinjaScriptProperty]
        [Display(Name = "Show Profile", Order = 1, GroupName = "Display")]
        public bool ShowProfile
        { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Number of Levels", Order = 2, GroupName = "Display")]
        public int NumberOfLevels
        { get; set; }

        [NinjaScriptProperty]
        [Range(50, 90)]
        [Display(Name = "Value Area %", Order = 3, GroupName = "Display")]
        public int ValueAreaPercentage
        { get; set; }

        [NinjaScriptProperty]
        [Range(20, 300)]
        [Display(Name = "Max Bar Width", Order = 4, GroupName = "Display")]
        public int MaxBarWidth
        { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Profile X Offset", Order = 5, GroupName = "Display")]
        public int ProfileXOffset
        { get; set; }

        [XmlIgnore]
        [Display(Name = "POC Color", Order = 6, GroupName = "Colors")]
        public System.Windows.Media.Brush POCBrush
        { get; set; }

        [Browsable(false)]
        public string POCBrushSerialize
        {
            get { return Serialize.BrushToString(POCBrush); }
            set { POCBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Value Area Color", Order = 7, GroupName = "Colors")]
        public System.Windows.Media.Brush ValueAreaBrush
        { get; set; }

        [Browsable(false)]
        public string ValueAreaBrushSerialize
        {
            get { return Serialize.BrushToString(ValueAreaBrush); }
            set { ValueAreaBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Outside Color", Order = 8, GroupName = "Colors")]
        public System.Windows.Media.Brush OutsideBrush
        { get; set; }

        [Browsable(false)]
        public string OutsideBrushSerialize
        {
            get { return Serialize.BrushToString(OutsideBrush); }
            set { OutsideBrush = Serialize.StringToBrush(value); }
        }

        // Plots for visibility in Data Box
        [Browsable(false)]
        public Series<double> POC
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        public Series<double> VAH
        {
            get { return Values[1]; }
        }

        [Browsable(false)]
        public Series<double> VAL
        {
            get { return Values[2]; }
        }

        #endregion
    }
}
