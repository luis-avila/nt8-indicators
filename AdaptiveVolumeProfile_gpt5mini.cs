#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = SharpDX.Color;
using FontWeight = SharpDX.DirectWrite.FontWeight;
using SolidColorBrush = SharpDX.Direct2D1.SolidColorBrush;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class AdaptiveVolumeProfile : Indicator
	{
		private Dictionary<double, long> volumeByPrice;
		private List<double> sortedPrices;
		private SessionIterator sessionIterator;
		private double totalVolume;
		private double pocPrice;
		private long pocVolume;
		private double vah;
		private double val;
		private bool profileVisible;

		private SolidColorBrush pocDxBrush;
		private SolidColorBrush valueAreaDxBrush;
		private SolidColorBrush otherDxBrush;
		private SolidColorBrush outlineDxBrush;
		private TextFormat textFormat;

		private Brush pocBrush;
		private Brush valueAreaBrush;
		private Brush otherBrush;
		private Brush outlineBrush;

		private int sessionStartBar = -1;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Real-time session volume profile rendered with SharpDX.";
				Name = "AdaptiveVolumeProfile";
				Calculate = Calculate.OnEachTick;
				IsOverlay = true;
				DrawOnPricePanel = true;
				DrawHorizontalGridLines = false;
				DrawVerticalGridLines = false;
				PaintPriceMarkers = false;
				ScaleJustification = ScaleJustification.Right;
				IsSuspendedWhileInactive = true;
				DisplayInDataBox = false;
				AddPlot(Brushes.Transparent, "AVP");
				ProfileVisible = true;
				Rows = 60;
				ValueAreaPercentage = 70;
				ProfileWidthFraction = 0.30;
				PocBrush = Brushes.Gold;
				ValueAreaBrush = Brushes.DodgerBlue;
				OtherBrush = Brushes.DimGray;
				OutlineBrush = Brushes.Transparent;
			}
			else if (State == State.DataLoaded)
			{
				volumeByPrice = new Dictionary<double, long>();
				sortedPrices = new List<double>();
				sessionIterator = new SessionIterator(Bars);
				sessionStartBar = -1;
				EnsureDxResources();
			}
			else if (State == State.Terminated)
			{
				DisposeDxResources();
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 0)
				return;

			if (BarsInProgress != 0)
				return;

			if (Bars.IsFirstBarOfSession)
				ResetSession();

			if (CurrentBar < 1)
				return;

			double price = Math.Round(Close[0] / TickSize) * TickSize;
			long vol = Math.Max(1, (long)Volume[0]);
			AccumulateVolume(price, vol);
			RecalculateProfile();
			Values[0][0] = pocPrice;
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (!ProfileVisible || ChartBars == null || RenderTarget == null || ChartPanel == null || Bars == null)
				return;

			if (volumeByPrice == null || volumeByPrice.Count == 0)
				return;

			int first = Math.Max(ChartBars.FromIndex, 0);
			int last = Math.Min(ChartBars.ToIndex, CurrentBar);
			if (first > last)
				return;

			float panelRight = ChartPanel.X + ChartPanel.Width;
			float panelTop = ChartPanel.Y;
			float panelBottom = ChartPanel.Y + ChartPanel.Height;
			float maxBarWidth = (float)(ChartPanel.Width * ProfileWidthFraction);
			float baseX = panelRight - maxBarWidth;
			float barHeight;
			float y;

			EnsureDxResources();
			if (pocDxBrush == null || valueAreaDxBrush == null || otherDxBrush == null)
				return;

			float maxVolume = (float)Math.Max(1, pocVolume);
			for (int i = 0; i < sortedPrices.Count; i++)
			{
				double price = sortedPrices[i];
				long vol = volumeByPrice[price];
				float width = (float)((vol / maxVolume) * maxBarWidth);
				if (width < 1f)
					width = 1f;

				bool isPoc = price.ApproxCompare(pocPrice) == 0;
				bool isValueArea = !isPoc && price >= val && price <= vah;
				SolidColorBrush dxBrush = isPoc ? pocDxBrush : (isValueArea ? valueAreaDxBrush : otherDxBrush);

				y = (float)chartScale.GetYByValue(price + TickSize * 0.5);
				float y2 = (float)chartScale.GetYByValue(price - TickSize * 0.5);
				barHeight = Math.Max(1f, Math.Abs(y2 - y));
				if (y + barHeight < panelTop || y > panelBottom)
					continue;

				var rect = new RectangleF(baseX, Math.Min(y, y2), width, barHeight);
				RenderTarget.FillRectangle(rect, dxBrush);
				if (outlineDxBrush != null)
					RenderTarget.DrawRectangle(rect, outlineDxBrush, 1f);
			}
		}

		public override void OnRenderTargetChanged()
		{
			DisposeDxResources();
			EnsureDxResources();
		}

		private void EnsureDxResources()
		{
			if (RenderTarget == null)
				return;

			if (pocDxBrush == null)
				pocDxBrush = PocBrush.ToDxBrush(RenderTarget) as SolidColorBrush;
			if (valueAreaDxBrush == null)
				valueAreaDxBrush = ValueAreaBrush.ToDxBrush(RenderTarget) as SolidColorBrush;
			if (otherDxBrush == null)
				otherDxBrush = OtherBrush.ToDxBrush(RenderTarget) as SolidColorBrush;
			if (outlineDxBrush == null && OutlineBrush != null)
				outlineDxBrush = OutlineBrush.ToDxBrush(RenderTarget) as SolidColorBrush;
			if (textFormat == null)
				textFormat = new TextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", FontWeight.Normal, FontStyle.Normal, 12f);
		}

		private void DisposeDxResources()
		{
			if (pocDxBrush != null) { pocDxBrush.Dispose(); pocDxBrush = null; }
			if (valueAreaDxBrush != null) { valueAreaDxBrush.Dispose(); valueAreaDxBrush = null; }
			if (otherDxBrush != null) { otherDxBrush.Dispose(); otherDxBrush = null; }
			if (outlineDxBrush != null) { outlineDxBrush.Dispose(); outlineDxBrush = null; }
			if (textFormat != null) { textFormat.Dispose(); textFormat = null; }
		}

		private void ResetSession()
		{
			volumeByPrice.Clear();
			sortedPrices.Clear();
			totalVolume = 0;
			pocPrice = double.NaN;
			pocVolume = 0;
			vah = double.NaN;
			val = double.NaN;
			sessionStartBar = CurrentBar;
		}

		private void AccumulateVolume(double price, long volume)
		{
			if (volume <= 0)
				return;

			if (!volumeByPrice.ContainsKey(price))
			{
				volumeByPrice[price] = 0;
				sortedPrices.Add(price);
				sortedPrices.Sort();
			}

			volumeByPrice[price] += volume;
			totalVolume += volume;
		}

		private void RecalculateProfile()
		{
			if (volumeByPrice.Count == 0)
				return;

			long maxVol = -1;
			double maxPrice = double.NaN;
			foreach (var kvp in volumeByPrice)
			{
				if (kvp.Value > maxVol)
				{
					maxVol = kvp.Value;
					maxPrice = kvp.Key;
				}
			}

			pocPrice = maxPrice;
			pocVolume = maxVol;
			CalculateValueArea();
		}

		private void CalculateValueArea()
		{
			if (sortedPrices.Count == 0 || double.IsNaN(pocPrice))
				return;

			double target = totalVolume * (ValueAreaPercentage / 100.0);
			double accumulated = pocVolume;
			double currentVah = pocPrice;
			double currentVal = pocPrice;
			int pocIndex = sortedPrices.IndexOf(pocPrice);
			int upperIndex = pocIndex + 1;
			int lowerIndex = pocIndex - 1;

			while (accumulated < target && (upperIndex < sortedPrices.Count || lowerIndex >= 0))
			{
				long above = upperIndex < sortedPrices.Count ? volumeByPrice[sortedPrices[upperIndex]] : 0;
				long below = lowerIndex >= 0 ? volumeByPrice[sortedPrices[lowerIndex]] : 0;
				if (above == 0 && below == 0)
					break;
				if (above >= below)
				{
					accumulated += above;
					currentVah = sortedPrices[upperIndex];
					upperIndex++;
				}
				else
				{
					accumulated += below;
					currentVal = sortedPrices[lowerIndex];
					lowerIndex--;
				}
			}

			vah = currentVah;
			val = currentVal;
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name = "ProfileVisible", Order = 1, GroupName = "Parameters")]
		public bool ProfileVisible
		{
			get => profileVisible;
			set => profileVisible = value;
		}

		[NinjaScriptProperty]
		[Range(10, 200)]
		[Display(Name = "Rows", Order = 2, GroupName = "Parameters")]
		public int Rows { get; set; }

		[NinjaScriptProperty]
		[Range(50, 95)]
		[Display(Name = "ValueAreaPercentage", Order = 3, GroupName = "Parameters")]
		public int ValueAreaPercentage { get; set; }

		[NinjaScriptProperty]
		[Range(0.05, 0.50)]
		[Display(Name = "ProfileWidthFraction", Order = 4, GroupName = "Parameters")]
		public double ProfileWidthFraction { get; set; }

		[XmlIgnore]
		[Display(Name = "POC Brush", Order = 5, GroupName = "Visual")]
		public Brush PocBrush
		{
			get => pocBrush;
			set => pocBrush = value;
		}

		[Browsable(false)]
		public string PocBrushSerialize
		{
			get => Serialize.BrushToString(PocBrush);
			set => PocBrush = Serialize.StringToBrush(value);
		}

		[XmlIgnore]
		[Display(Name = "Value Area Brush", Order = 6, GroupName = "Visual")]
		public Brush ValueAreaBrush
		{
			get => valueAreaBrush;
			set => valueAreaBrush = value;
		}

		[Browsable(false)]
		public string ValueAreaBrushSerialize
		{
			get => Serialize.BrushToString(ValueAreaBrush);
			set => ValueAreaBrush = Serialize.StringToBrush(value);
		}

		[XmlIgnore]
		[Display(Name = "Other Brush", Order = 7, GroupName = "Visual")]
		public Brush OtherBrush
		{
			get => otherBrush;
			set => otherBrush = value;
		}

		[Browsable(false)]
		public string OtherBrushSerialize
		{
			get => Serialize.BrushToString(OtherBrush);
			set => OtherBrush = Serialize.StringToBrush(value);
		}

		[XmlIgnore]
		[Display(Name = "Outline Brush", Order = 8, GroupName = "Visual")]
		public Brush OutlineBrush
		{
			get => outlineBrush;
			set => outlineBrush = value;
		}

		[Browsable(false)]
		public string OutlineBrushSerialize
		{
			get => Serialize.BrushToString(OutlineBrush);
			set => OutlineBrush = Serialize.StringToBrush(value);
		}
		#endregion
	}
}
