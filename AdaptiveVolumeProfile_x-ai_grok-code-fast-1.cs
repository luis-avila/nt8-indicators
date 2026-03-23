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
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AdaptiveVolumeProfile : Indicator
    {
        // Private variables
        private Dictionary<double, long> volumeProfile;
        private SharpDX.Direct2D1.SolidColorBrush pocBrush;
        private SharpDX.Direct2D1.SolidColorBrush vaBrush;
        private SharpDX.Direct2D1.SolidColorBrush otherBrush;
        private double pocPrice;
        private double vah;
        private double val;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                 = @"Real-time volume profile for current session with POC and Value Area highlighting";
                Name                        = "AdaptiveVolumeProfile";
                Calculate                   = Calculate.OnEachTick;
                IsOverlay                   = false;
                DisplayInDataBox            = true;
                DrawOnPricePanel            = false;
                DrawHorizontalGridLines     = true;
                DrawVerticalGridLines       = true;
                PaintPriceMarkers           = true;
                ScaleJustification          = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive    = true;

                NumberOfLevels = 100;
                ShowProfile = true;
                MaxBarWidth = 100;
                PocBrush = Brushes.Red;
                VaBrush = Brushes.Yellow;
                OtherBrush = Brushes.Gray;
            }
            else if (State == State.DataLoaded)
            {
                // Initialize Series<T> and cached indicator references here
                volumeProfile = new Dictionary<double, long>();
            }
        }

        protected override void OnBarUpdate()
        {
            // Session reset
            if (Bars.IsFirstBarOfSession)
            {
                volumeProfile.Clear();
                pocPrice = double.NaN;
                vah = double.NaN;
                val = double.NaN;
            }

            if (CurrentBar < 0)
                return;

            // Accumulate volume at current price level
            double bucketPrice = BucketPrice(Close[0]);
            if (!volumeProfile.ContainsKey(bucketPrice))
                volumeProfile[bucketPrice] = 0;
            volumeProfile[bucketPrice] += (long)Volume[0];

            // Calculate POC
            if (volumeProfile.Count > 0)
            {
                pocPrice = volumeProfile.OrderByDescending(x => x.Value).First().Key;
            }

            // Calculate Value Area
            CalculateValueArea();
        }

        private double BucketPrice(double price)
        {
            return Math.Floor(price / TickSize) * TickSize;
        }

        private void CalculateValueArea()
        {
            if (volumeProfile.Count == 0)
                return;

            long totalVolume = volumeProfile.Values.Sum();
            long targetVolume = (long)(totalVolume * 0.7); // 70%
            long accumulatedVolume = 0;

            // Get sorted price levels
            var sortedPrices = volumeProfile.OrderBy(x => x.Key).ToList();

            // Find POC index
            int pocIndex = sortedPrices.FindIndex(x => x.Key == pocPrice);

            // Expand symmetrically from POC
            int left = pocIndex;
            int right = pocIndex;
            accumulatedVolume += sortedPrices[pocIndex].Value;

            while (accumulatedVolume < targetVolume && (left > 0 || right < sortedPrices.Count - 1))
            {
                if (left > 0)
                {
                    left--;
                    accumulatedVolume += sortedPrices[left].Value;
                }
                if (accumulatedVolume >= targetVolume)
                    break;
                if (right < sortedPrices.Count - 1)
                {
                    right++;
                    accumulatedVolume += sortedPrices[right].Value;
                }
            }

            val = sortedPrices[left].Key;
            vah = sortedPrices[right].Key;
        }

        protected override void OnRenderTargetChanged()
        {
            if (pocBrush != null && !pocBrush.IsDisposed)
                pocBrush.Dispose();
            if (vaBrush != null && !vaBrush.IsDisposed)
                vaBrush.Dispose();
            if (otherBrush != null && !otherBrush.IsDisposed)
                otherBrush.Dispose();

            if (RenderTarget != null)
            {
                pocBrush = PocBrush.ToDxBrush(RenderTarget);
                vaBrush = VaBrush.ToDxBrush(RenderTarget);
                otherBrush = OtherBrush.ToDxBrush(RenderTarget);
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (!ShowProfile || volumeProfile.Count == 0 || RenderTarget == null || pocBrush == null || vaBrush == null || otherBrush == null)
                return;

            // Get visible price range
            double chartLow = chartScale.GetValueByY(ChartPanel.Y + ChartPanel.H);
            double chartHigh = chartScale.GetValueByY(ChartPanel.Y);

            // Filter to visible levels
            var visibleLevels = volumeProfile.Where(kvp => kvp.Key >= chartLow && kvp.Key <= chartHigh).ToList();
            long maxVolume = visibleLevels.Count > 0 ? visibleLevels.Max(x => x.Value) : 0;
            if (maxVolume == 0)
                return;

            // Sort by volume descending for level limit
            var topLevels = visibleLevels.OrderByDescending(x => x.Value).Take(NumberOfLevels).OrderBy(x => x.Key).ToList();

            double tickPixelHeight = chartScale.GetYByValue(BucketPrice(chartHigh)) - chartScale.GetYByValue(BucketPrice(chartHigh + TickSize));

            foreach (var kvp in topLevels)
            {
                double price = kvp.Key;
                long volume = kvp.Value;

                SharpDX.Direct2D1.Brush brush = (price == pocPrice) ? pocBrush : (price >= val && price <= vah) ? vaBrush : otherBrush;

                float barWidth = (float)((volume / (double)maxVolume) * MaxBarWidth);
                float xLeft = ChartPanel.X + ChartPanel.W - barWidth;
                float yTop = chartScale.GetYByValue(price);
                float yBottom = yTop + Math.Abs(tickPixelHeight);

                SharpDX.RectangleF rect = new SharpDX.RectangleF(xLeft, yTop, barWidth, yBottom - yTop);

                RenderTarget.FillRectangle(rect, brush);
            }
        }

        #region Properties

        [NinjaScriptProperty]
        [Range(10, 1000)]
        [Display(Name = "Number Of Levels", Description = "Maximum number of price levels to display", Order = 1, GroupName = "Parameters")]
        public int NumberOfLevels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Profile", Description = "Toggle display of volume profile", Order = 2, GroupName = "Parameters")]
        public bool ShowProfile { get; set; }

        [NinjaScriptProperty]
        [Range(50, 500)]
        [Display(Name = "Max Bar Width", Description = "Maximum width of volume bars in pixels", Order = 3, GroupName = "Parameters")]
        public int MaxBarWidth { get; set; }

        [XmlIgnore]
        [Display(Name = "POC Brush", Description = "Color for Point of Control level", Order = 1, GroupName = "Colors")]
        public Brush PocBrush { get; set; }

        [Browsable(false)]
        public string PocBrushSerialize
        {
            get { return Serialize.BrushToString(PocBrush); }
            set { PocBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Value Area Brush", Description = "Color for Value Area levels", Order = 2, GroupName = "Colors")]
        public Brush VaBrush { get; set; }

        [Browsable(false)]
        public string VaBrushSerialize
        {
            get { return Serialize.BrushToString(VaBrush); }
            set { VaBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Other Levels Brush", Description = "Color for remaining price levels", Order = 3, GroupName = "Colors")]
        public Brush OtherBrush { get; set; }

        [Browsable(false)]
        public string OtherBrushSerialize
        {
            get { return Serialize.BrushToString(OtherBrush); }
            set { OtherBrush = Serialize.StringToBrush(value); }
        }

        #endregion

        // Cleanup resources
        protected override void OnTermination()
        {
            if (pocBrush != null && !pocBrush.IsDisposed)
                pocBrush.Dispose();
            if (vaBrush != null && !vaBrush.IsDisposed)
                vaBrush.Dispose();
            if (otherBrush != null && !otherBrush.IsDisposed)
                otherBrush.Dispose();
        }
    }
}