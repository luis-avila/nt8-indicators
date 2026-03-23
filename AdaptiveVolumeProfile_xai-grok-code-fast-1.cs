using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
using SharpDX;
using SharpDX.Direct2D1;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AdaptiveVolumeProfile_xai_grok_code_fast_1 : Indicator
    {
        private SessionIterator sessionIterator;
        private DateTime previousSessionBegin;
        private Dictionary<double, long> volumeProfile;
        private double pocPrice, vah, val;
        private long maxVolume;
        private double minPrice = double.MaxValue;
        private double maxPrice = double.MinValue;

        private SolidColorBrush pocBrushDx, vaBrushDx, otherBrushDx;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Calculates a real-time volume profile for the current session and displays it as horizontal bars on the right side of the price panel.";
                Name = "AdaptiveVolumeProfile";
                Calculate = Calculate.OnEachTick;
                IsOverlay = false;
                DisplayInDataBox = false;
                DrawOnPricePanel = false;
                DrawHorizontalGridLines = false;
                DrawVerticalGridLines = false;
                PaintPriceMarkers = false;
                ScaleJustification = ScaleJustification.Left;
                IsSuspendedWhileInactive = true;

                // Default properties
                NumberOfLevels = 20;
                ShowProfile = true;
                ValueAreaPercentage = 70;
                ProfileXOffset = 10;
                MaxBarWidth = 50;
                TickSizeModifier = 1;

                POCBrush = Brushes.Blue;
                VABrush = Brushes.Green;
                OtherBrush = Brushes.Red;
            }
            else if (State == State.DataLoaded)
            {
                sessionIterator = new SessionIterator(Bars);
                volumeProfile = new Dictionary<double, long>();
                previousSessionBegin = sessionIterator.ActualSessionBegin;
            }
            else if (State == State.Terminated)
            {
                if (pocBrushDx != null) pocBrushDx.Dispose();
                if (vaBrushDx != null) vaBrushDx.Dispose();
                if (otherBrushDx != null) otherBrushDx.Dispose();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 0) return;

            DateTime currentSessionBegin = sessionIterator.ActualSessionBegin;
            if (currentSessionBegin != previousSessionBegin)
            {
                volumeProfile.Clear();
                minPrice = double.MaxValue;
                maxPrice = double.MinValue;
                previousSessionBegin = currentSessionBegin;
                pocPrice = vah = val = 0;
                maxVolume = 0;
            }

            double levelSize = TickSize * TickSizeModifier;
            double currentPrice = Math.Round(Close[0] / levelSize) * levelSize;
            
            if (!volumeProfile.ContainsKey(currentPrice))
                volumeProfile[currentPrice] = 0;
            volumeProfile[currentPrice] += Volume[0];

            minPrice = Math.Min(minPrice, currentPrice);
            maxPrice = Math.Max(maxPrice, currentPrice);

            RecalculatePOCAndVA();
        }

        private void RecalculatePOCAndVA()
        {
            if (volumeProfile.Count == 0) return;

            var maxKV = volumeProfile.Aggregate((l, r) => l.Value > r.Value ? l : r);
            pocPrice = maxKV.Key;
            maxVolume = maxKV.Value;

            long totalVolume = volumeProfile.Values.Sum();
            long targetVol = (long)(totalVolume * ValueAreaPercentage / 100.0);
            long accumulatedVol = maxVolume;
            List<double> vaPrices = new List<double> { pocPrice };

            var sortedPrices = volumeProfile.OrderBy(kv => Math.Abs(kv.Key - pocPrice)).ThenBy(kv => kv.Key);
            foreach (var kv in sortedPrices.Where(kv => kv.Key != pocPrice))
            {
                if (accumulatedVol >= targetVol) break;
                vaPrices.Add(kv.Key);
                accumulatedVol += kv.Value;
            }
            vah = vaPrices.Max();
            val = vaPrices.Min();
        }

        protected override void OnRenderTargetChanged()
        {
            base.OnRenderTargetChanged();

            if (pocBrushDx != null) pocBrushDx.Dispose();
            if (vaBrushDx != null) vaBrushDx.Dispose();
            if (otherBrushDx != null) otherBrushDx.Dispose();

            if (RenderTarget != null)
            {
                pocBrushDx = POCBrush.ToDxBrush(RenderTarget);
                vaBrushDx = VABrush.ToDxBrush(RenderTarget);
                otherBrushDx = OtherBrush.ToDxBrush(RenderTarget);
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (!ShowProfile || volumeProfile.Count == 0 || pocBrushDx == null || vaBrushDx == null || otherBrushDx == null) return;

            if (ChartBars == null) return;
            float xRight = (float)(ChartPanel.X + ChartPanel.Width - ProfileXOffset);
            double levelSize = TickSize * TickSizeModifier;
            var displayLevels = volumeProfile.ToList();

            if (NumberOfLevels > 0)
                displayLevels = volumeProfile.OrderByDescending(kv => kv.Value).Take(NumberOfLevels).ToList();

            float rowHeight = Math.Min(chartControl.BarWidth * 0.8f, 5f);

            foreach (var kv in displayLevels)
            {
                float yTop = chartScale.GetYByValue(kv.Key);
                if (yTop < ChartPanel.Y || yTop > ChartPanel.Y + ChartPanel.Height) continue; // only visible

                float barWidth = Math.Max((float)(kv.Value / (double)maxVolume) * MaxBarWidth, 1f);
                SolidColorBrush useBrush = otherBrushDx;
                if (Math.Abs(kv.Key - pocPrice) < levelSize / 2)
                    useBrush = pocBrushDx;
                else if (kv.Key >= val && kv.Key <= vah)
                    useBrush = vaBrushDx;

                Vector2 bottomLeft = new Vector2(xRight - barWidth, yTop - rowHeight / 2);
                Vector2 topRight = new Vector2(xRight, yTop + rowHeight / 2);
                RectangleF rect = new RectangleF(bottomLeft.X, bottomLeft.Y, barWidth, rowHeight);

                RenderTarget.FillRectangle(rect, useBrush);
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name="Number of Levels", Order=1, GroupName="Parameters")]
        public int NumberOfLevels
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Show Profile", Order=2, GroupName="Parameters")]
        public bool ShowProfile
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Value Area Percentage", Order=3, GroupName="Parameters")]
        public int ValueAreaPercentage
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Profile X Offset", Order=4, GroupName="Parameters")]
        public int ProfileXOffset
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Max Bar Width", Order=5, GroupName="Parameters")]
        public int MaxBarWidth
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Tick Size Modifier", Order=6, GroupName="Parameters")]
        public int TickSizeModifier
        { get; set; }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="POC Brush", Order=7, GroupName="Bars")]
        public Brush POCBrush
        { get; set; }

        [Browsable(false)]
        public string POCBrushSerialize
        {
            get { return Serialize.BrushToString(POCBrush); }
            set { POCBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="VA Brush", Order=8, GroupName="Bars")]
        public Brush VABrush
        { get; set; }

        [Browsable(false)]
        public string VABrushSerialize
        {
            get { return Serialize.BrushToString(VABrush); }
            set { VABrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="Other Brush", Order=9, GroupName="Bars")]
        public Brush OtherBrush
        { get; set; }

        [Browsable(false)]
        public string OtherBrushSerialize
        {
            get { return Serialize.BrushToString(OtherBrush); }
            set { OtherBrush = Serialize.StringToBrush(value); }
        }
        #endregion
    }
}