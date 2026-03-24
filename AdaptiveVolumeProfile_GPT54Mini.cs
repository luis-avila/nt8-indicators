#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX;
using SharpDX.Direct2D1;
#endregion

// This namespace holds indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
{
	public class AdaptiveVolumeProfile : Indicator
	{
		private const double ValueAreaPercentDefault = 0.70;
		private SessionIterator sessionIterator;
		private bool sessionInitialized;
		private DateTime currentSessionBegin = Core.Globals.MinDate;
		private Dictionary<double, double> sessionVolumeByPrice;
		private double lastAccumulatedBarVolume;
		private double lastProcessedPrice;
		private int lastProcessedBar = -1;
		private bool needsRebuild;

		private SolidColorBrush pocBrushDx;
		private SolidColorBrush valueAreaBrushDx;
		private SolidColorBrush otherBrushDx;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"Real-time current-session volume profile rendered with SharpDX.";
				Name = "AdaptiveVolumeProfile";
				Calculate = Calculate.OnEachTick;
				IsOverlay = true;
				DisplayInDataBox = false;
				DrawOnPricePanel = true;
				DrawHorizontalGridLines = true;
				DrawVerticalGridLines = true;
				PaintPriceMarkers = false;
				ScaleJustification = ScaleJustification.Right;
				IsSuspendedWhileInactive = true;
				BarsRequiredToPlot = 0;
				ShowProfile = true;
				Rows = 48;
				ValueAreaPercent = 70;
				PocOpacity = 90;
				ValueAreaOpacity = 70;
				OtherOpacity = 45;
				PocBrush = Brushes.Gold;
				ValueAreaBrush = Brushes.DodgerBlue;
				OtherBrush = Brushes.DimGray;
			}
			else if (State == State.DataLoaded)
			{
				sessionIterator = new SessionIterator(Bars);
				sessionVolumeByPrice = new Dictionary<double, double>();
				needsRebuild = true;
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

			try
			{
				if (Bars.IsFirstBarOfSession)
					ResetSession();

				double barVolume = Instrument.MasterInstrument.InstrumentType == InstrumentType.CryptoCurrency
					? Core.Globals.ToCryptocurrencyVolume((long)Volume[0])
					: Volume[0];

				if (barVolume < 0)
					barVolume = 0;

				if (CurrentBar != lastProcessedBar)
				{
					lastProcessedBar = CurrentBar;
					lastProcessedPrice = RoundToTick(Close[0]);
					lastAccumulatedBarVolume = 0;
				}

				double delta = barVolume - lastAccumulatedBarVolume;
				if (delta < 0)
					delta = barVolume;
				lastAccumulatedBarVolume = barVolume;

				if (delta > 0)
				{
					double bucketPrice = RoundToTick(lastProcessedPrice);
					if (!sessionVolumeByPrice.ContainsKey(bucketPrice))
						sessionVolumeByPrice[bucketPrice] = 0;
					sessionVolumeByPrice[bucketPrice] += delta;
				}

				needsRebuild = true;
			}
			catch (Exception ex)
			{
				Log($"AdaptiveVolumeProfile OnBarUpdate error: {ex.Message}", LogLevel.Error);
			}
		}

		protected override void OnRenderTargetChanged()
		{
			DisposeDxResources();

			if (RenderTarget == null)
				return;

			pocBrushDx = PocBrush.ToDxBrush(RenderTarget) as SolidColorBrush;
			valueAreaBrushDx = ValueAreaBrush.ToDxBrush(RenderTarget) as SolidColorBrush;
			otherBrushDx = OtherBrush.ToDxBrush(RenderTarget) as SolidColorBrush;
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (!ShowProfile || RenderTarget == null || ChartPanel == null || sessionVolumeByPrice == null || sessionVolumeByPrice.Count == 0)
				return;

			try
			{
				var profile = BuildProfile();
				if (profile.Count == 0)
					return;

				float rightEdge = ChartPanel.X + ChartPanel.Width;
				float maxBarWidth = Math.Max(20f, Math.Min(120f, ChartPanel.Width * 0.22f));
				float topMargin = 2f;
				float bottomMargin = 2f;

				foreach (var level in profile)
				{
					float yTop = chartScale.GetYByValue(level.Price + TickSize * 0.5);
					float yBottom = chartScale.GetYByValue(level.Price - TickSize * 0.5);
					if (float.IsNaN(yTop) || float.IsNaN(yBottom))
						continue;

					float top = Math.Min(yTop, yBottom) + topMargin;
					float height = Math.Max(1f, Math.Abs(yBottom - yTop) - topMargin - bottomMargin);
					float width = (float)(level.Volume / profile.MaxVolume * maxBarWidth);
					float left = rightEdge - width;
					var rect = new RectangleF(left, top, width, height);
					var brush = level.IsPoc ? pocBrushDx : level.IsValueArea ? valueAreaBrushDx : otherBrushDx;
					if (brush == null || brush.IsDisposed)
						continue;

					RenderTarget.FillRectangle(rect, brush);
				}
			}
			catch (Exception ex)
			{
				Log($"AdaptiveVolumeProfile OnRender error: {ex.Message}", LogLevel.Error);
			}
		}

		private void ResetSession()
		{
			sessionVolumeByPrice.Clear();
			lastAccumulatedBarVolume = 0;
			lastProcessedBar = -1;
			currentSessionBegin = Time[0];
			needsRebuild = true;
		}

		private double RoundToTick(double price)
		{
			if (TickSize <= 0)
				return price;
			return Instrument.MasterInstrument.RoundToTickSize(price);
		}

		private List<ProfileLevel> BuildProfile()
		{
			if (sessionVolumeByPrice.Count == 0)
				return new List<ProfileLevel>();

			double minPrice = double.MaxValue;
			double maxPrice = double.MinValue;
			double totalVolume = 0;
			double maxVolume = 0;
			double pocPrice = double.NaN;

			foreach (var kvp in sessionVolumeByPrice)
			{
				minPrice = Math.Min(minPrice, kvp.Key);
				maxPrice = Math.Max(maxPrice, kvp.Key);
				totalVolume += kvp.Value;
				if (kvp.Value > maxVolume)
				{
					maxVolume = kvp.Value;
					pocPrice = kvp.Key;
				}
			}

			if (maxVolume <= 0 || double.IsNaN(pocPrice))
				return new List<ProfileLevel>();

			int desiredRows = Math.Max(1, Rows);
			double step = Math.Max(TickSize, Math.Round((maxPrice - minPrice) / desiredRows / TickSize) * TickSize);
			if (step <= 0)
				step = TickSize;

			var levels = new List<ProfileLevel>();
			for (double p = minPrice; p <= maxPrice + (step * 0.5); p += step)
			{
				double bucketPrice = RoundToTick(p);
				double volume = 0;
				foreach (var kvp in sessionVolumeByPrice)
				{
					if (Math.Abs(kvp.Key - bucketPrice) < TickSize * 0.5)
						volume += kvp.Value;
				}
				levels.Add(new ProfileLevel { Price = bucketPrice, Volume = volume });
			}

			int pocIndex = -1;
			for (int i = 0; i < levels.Count; i++)
				if (Math.Abs(levels[i].Price - pocPrice) < TickSize * 0.5)
				{
					pocIndex = i;
					break;
				}
			if (pocIndex < 0)
				pocIndex = levels.FindIndex(l => l.Volume == maxVolume);
			if (pocIndex < 0)
				pocIndex = 0;

			double targetVolume = totalVolume * Math.Max(0.01, Math.Min(1.0, ValueAreaPercent / 100.0));
			double vaVolume = levels[pocIndex].Volume;
			levels[pocIndex].IsPoc = true;
			levels[pocIndex].IsValueArea = true;

			int lower = pocIndex - 1;
			int upper = pocIndex + 1;
			while (vaVolume < targetVolume && (lower >= 0 || upper < levels.Count))
			{
				double lowerVol = lower >= 0 ? levels[lower].Volume : double.MinValue;
				double upperVol = upper < levels.Count ? levels[upper].Volume : double.MinValue;
				bool takeUpper = upperVol >= lowerVol;
				if (takeUpper && upper < levels.Count)
				{
					levels[upper].IsValueArea = true;
					vaVolume += levels[upper].Volume;
					upper++;
				}
				else if (lower >= 0)
				{
					levels[lower].IsValueArea = true;
					vaVolume += levels[lower].Volume;
					lower--;
				}
				else
					break;
			}

			return new List<ProfileLevel>(levels);
		}

		private void DisposeDxResources()
		{
			if (pocBrushDx != null && !pocBrushDx.IsDisposed) pocBrushDx.Dispose();
			if (valueAreaBrushDx != null && !valueAreaBrushDx.IsDisposed) valueAreaBrushDx.Dispose();
			if (otherBrushDx != null && !otherBrushDx.IsDisposed) otherBrushDx.Dispose();
			pocBrushDx = null;
			valueAreaBrushDx = null;
			otherBrushDx = null;
		}

		private class ProfileLevel
		{
			public double Price;
			public double Volume;
			public bool IsPoc;
			public bool IsValueArea;
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name = "ShowProfile", Order = 1, GroupName = "Parameters")]
		public bool ShowProfile { get; set; }

		[NinjaScriptProperty]
		[Range(5, 500)]
		[Display(Name = "Rows", Order = 2, GroupName = "Parameters")]
		public int Rows { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "ValueAreaPercent", Order = 3, GroupName = "Parameters")]
		public double ValueAreaPercent { get; set; }

		[XmlIgnore]
		[Display(Name = "POC Brush", Order = 10, GroupName = "Visual")]
		public Brush PocBrush { get; set; }

		[Browsable(false)]
		public string PocBrushSerialize
		{
			get => Serialize.BrushToString(PocBrush);
			set => PocBrush = Serialize.StringToBrush(value);
		}

		[XmlIgnore]
		[Display(Name = "Value Area Brush", Order = 11, GroupName = "Visual")]
		public Brush ValueAreaBrush { get; set; }

		[Browsable(false)]
		public string ValueAreaBrushSerialize
		{
			get => Serialize.BrushToString(ValueAreaBrush);
			set => ValueAreaBrush = Serialize.StringToBrush(value);
		}

		[XmlIgnore]
		[Display(Name = "Other Brush", Order = 12, GroupName = "Visual")]
		public Brush OtherBrush { get; set; }

		[Browsable(false)]
		public string OtherBrushSerialize
		{
			get => Serialize.BrushToString(OtherBrush);
			set => OtherBrush = Serialize.StringToBrush(value);
		}
		#endregion
	}
}
