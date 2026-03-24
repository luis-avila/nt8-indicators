#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using SharpDX;
using SharpDX.Direct2D1;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class AdaptiveVolumeProfile : Indicator
	{
		private struct PriceLevel
		{
			public double Price;
			public double Volume;
			public bool IsPoc;
			public bool IsValueArea;
		}

		private SessionIterator sessionIterator;
		private Dictionary<double, double> volumeByPrice;
		private bool sessionReady;
		private int lastSessionBar = -1;
		private SolidColorBrush pocDxBrush;
		private SolidColorBrush valueAreaDxBrush;
		private SolidColorBrush otherDxBrush;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Real-time current-session volume profile rendered on the right side of the price panel.";
				Name = "AdaptiveVolumeProfile";
				Calculate = Calculate.OnEachTick;
				IsOverlay = true;
				DrawOnPricePanel = true;
				DisplayInDataBox = false;
				PaintPriceMarkers = false;
				IsSuspendedWhileInactive = true;
				BarsRequiredToPlot = 0;
				ShowProfile = true;
				Rows = 48;
				ValueAreaPercent = 70;
				PocBrush = Brushes.Gold;
				ValueAreaBrush = Brushes.DodgerBlue;
				OtherBrush = Brushes.DimGray;
			}
			else if (State == State.DataLoaded)
			{
				sessionIterator = new SessionIterator(Bars);
				volumeByPrice = new Dictionary<double, double>();
				sessionReady = true;
			}
			else if (State == State.Terminated)
			{
				DisposeDxResources();
			}
		}

		protected override void OnBarUpdate()
		{
			if (!sessionReady || CurrentBar < 0)
				return;

			if (Bars.IsFirstBarOfSession && CurrentBar != lastSessionBar)
			{
				volumeByPrice.Clear();
				lastSessionBar = CurrentBar;
			}

			double volume = Instrument.MasterInstrument.InstrumentType == InstrumentType.CryptoCurrency
				? Core.Globals.ToCryptocurrencyVolume((long)Volume[0])
				: Volume[0];

			if (volume < 0)
				volume = 0;

			double price = Instrument.MasterInstrument.RoundToTickSize(Close[0]);
			if (volumeByPrice.TryGetValue(price, out double existing))
				volumeByPrice[price] = existing + volume;
			else
				volumeByPrice[price] = volume;
		}

		protected override void OnRenderTargetChanged()
		{
			DisposeDxResources();

			if (RenderTarget == null)
				return;

			pocDxBrush = PocBrush.ToDxBrush(RenderTarget) as SolidColorBrush;
			valueAreaDxBrush = ValueAreaBrush.ToDxBrush(RenderTarget) as SolidColorBrush;
			otherDxBrush = OtherBrush.ToDxBrush(RenderTarget) as SolidColorBrush;
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (!ShowProfile || RenderTarget == null || ChartPanel == null || volumeByPrice == null || volumeByPrice.Count == 0 || IsInHitTest)
				return;

			try
			{
				List<PriceLevel> profile = BuildProfile();
				if (profile.Count == 0)
					return;

				float rightEdge = ChartPanel.X + ChartPanel.Width;
				float maxBarWidth = Math.Max(20f, Math.Min(140f, ChartPanel.Width * 0.22f));

				foreach (PriceLevel level in profile)
				{
					float y1 = chartScale.GetYByValue(level.Price + TickSize * 0.5);
					float y2 = chartScale.GetYByValue(level.Price - TickSize * 0.5);
					if (float.IsNaN(y1) || float.IsNaN(y2))
						continue;

					float top = Math.Min(y1, y2);
					float height = Math.Max(1f, Math.Abs(y2 - y1) - 1f);
					float width = (float)(level.Volume / GetMaxVolume(profile) * maxBarWidth);
					float left = rightEdge - width;
					RectangleF rect = new RectangleF(left, top, width, height);

					SolidColorBrush brush = level.IsPoc ? pocDxBrush : level.IsValueArea ? valueAreaDxBrush : otherDxBrush;
					if (brush == null || brush.IsDisposed)
						continue;

					RenderTarget.FillRectangle(rect, brush);
				}
			}
			catch (Exception ex)
			{
				Log($"AdaptiveVolumeProfile render error: {ex.Message}", LogLevel.Error);
			}
		}

		private List<PriceLevel> BuildProfile()
		{
			var levels = new List<PriceLevel>();
			if (volumeByPrice.Count == 0)
				return levels;

			double minPrice = double.MaxValue;
			double maxPrice = double.MinValue;
			double totalVolume = 0;
			double maxVolume = 0;
			double pocPrice = double.NaN;

			foreach (KeyValuePair<double, double> kvp in volumeByPrice)
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
				return levels;

			int rowCount = Math.Max(1, Rows);
			double step = TickSize;
			if (maxPrice > minPrice && rowCount > 0)
			{
				double rawStep = (maxPrice - minPrice) / rowCount;
				int ticks = (int)Math.Max(1, Math.Round(rawStep / TickSize));
				step = Math.Max(TickSize, ticks * TickSize);
			}

			for (double price = minPrice; price <= maxPrice + (step * 0.5); price += step)
			{
				double bucketPrice = Instrument.MasterInstrument.RoundToTickSize(price);
				double bucketVolume = 0;

				for (double inner = bucketPrice; inner < bucketPrice + step; inner += TickSize)
				{
					double normalized = Instrument.MasterInstrument.RoundToTickSize(inner);
					if (volumeByPrice.TryGetValue(normalized, out double vol))
						bucketVolume += vol;
				}

				levels.Add(new PriceLevel { Price = bucketPrice, Volume = bucketVolume });
			}

			int pocIndex = 0;
			double currentMax = -1;
			for (int i = 0; i < levels.Count; i++)
			{
				if (levels[i].Volume > currentMax)
				{
					currentMax = levels[i].Volume;
					pocIndex = i;
				}
			}

			double targetVolume = totalVolume * (ValueAreaPercent / 100.0);
			double accumulated = levels[pocIndex].Volume;
			levels[pocIndex] = new PriceLevel { Price = levels[pocIndex].Price, Volume = levels[pocIndex].Volume, IsPoc = true, IsValueArea = true };

			int lower = pocIndex - 1;
			int upper = pocIndex + 1;
			while (accumulated < targetVolume && (lower >= 0 || upper < levels.Count))
			{
				double lowerVol = lower >= 0 ? levels[lower].Volume : double.MinValue;
				double upperVol = upper < levels.Count ? levels[upper].Volume : double.MinValue;

				if (upperVol >= lowerVol && upper < levels.Count)
				{
					var lvl = levels[upper];
					lvl.IsValueArea = true;
					levels[upper] = lvl;
					accumulated += lvl.Volume;
					upper++;
				}
				else if (lower >= 0)
				{
					var lvl = levels[lower];
					lvl.IsValueArea = true;
					levels[lower] = lvl;
					accumulated += lvl.Volume;
					lower--;
				}
				else
				{
					break;
				}
			}

			return levels;
		}

		private double GetMaxVolume(List<PriceLevel> profile)
		{
			double max = 0;
			for (int i = 0; i < profile.Count; i++)
				if (profile[i].Volume > max)
					max = profile[i].Volume;
			return max <= 0 ? 1 : max;
		}

		private void DisposeDxResources()
		{
			if (pocDxBrush != null && !pocDxBrush.IsDisposed)
				pocDxBrush.Dispose();
			if (valueAreaDxBrush != null && !valueAreaDxBrush.IsDisposed)
				valueAreaDxBrush.Dispose();
			if (otherDxBrush != null && !otherDxBrush.IsDisposed)
				otherDxBrush.Dispose();

			pocDxBrush = null;
			valueAreaDxBrush = null;
			otherDxBrush = null;
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name = "ShowProfile", Order = 1, GroupName = "Parameters")]
		public bool ShowProfile { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
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
			get { return Serialize.BrushToString(PocBrush); }
			set { PocBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Value Area Brush", Order = 11, GroupName = "Visual")]
		public Brush ValueAreaBrush { get; set; }

		[Browsable(false)]
		public string ValueAreaBrushSerialize
		{
			get { return Serialize.BrushToString(ValueAreaBrush); }
			set { ValueAreaBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Other Brush", Order = 12, GroupName = "Visual")]
		public Brush OtherBrush { get; set; }

		[Browsable(false)]
		public string OtherBrushSerialize
		{
			get { return Serialize.BrushToString(OtherBrush); }
			set { OtherBrush = Serialize.StringToBrush(value); }
		}
		#endregion
	}
}
