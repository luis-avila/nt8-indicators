#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
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
		private Dictionary<double, double> volumeByPrice;
		private List<ProfileRow> profileRows;
		private SharpDX.Direct2D1.Brush pocDxBrush;
		private SharpDX.Direct2D1.Brush valueAreaDxBrush;
		private SharpDX.Direct2D1.Brush outsideDxBrush;
		private SharpDX.Direct2D1.Brush borderDxBrush;
		private double pocPrice;
		private double valueAreaLow;
		private double valueAreaHigh;
		private double maxVolume;
		private double totalVolume;
		private bool profileDirty;
		private bool sessionInitialized;
		private TextFormat textFormat;

		private struct ProfileRow
		{
			public double Price;
			public double Volume;
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"Real-time session-only volume profile rendered with SharpDX.";
				Name = "AdaptiveVolumeProfile";
				Calculate = Calculate.OnEachTick;
				IsOverlay = true;
				DisplayInDataBox = false;
				DrawOnPricePanel = true;
				DrawHorizontalGridLines = false;
				DrawVerticalGridLines = false;
				PaintPriceMarkers = false;
				ScaleJustification = ScaleJustification.Right;
				IsSuspendedWhileInactive = true;
				Rows = 48;
				ShowProfile = true;
				ValueAreaPercentage = 70;
				PocBrush = Brushes.Gold;
				ValueAreaBrush = Brushes.DodgerBlue;
				OutsideBrush = Brushes.DimGray;
				BorderBrush = Brushes.Transparent;
			}
			else if (State == State.DataLoaded)
			{
				volumeByPrice = new Dictionary<double, double>();
				profileRows = new List<ProfileRow>();
				sessionInitialized = false;
				profileDirty = true;
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

			if (Bars.IsFirstBarOfSession)
				ResetSessionData();

			if (Volume[0] <= 0)
				return;

			double price = Math.Round(Close[0] / TickSize) * TickSize;
			if (volumeByPrice.ContainsKey(price))
				volumeByPrice[price] += Volume[0];
			else
				volumeByPrice[price] = Volume[0];

			profileDirty = true;
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (!ShowProfile || chartControl == null || chartScale == null || ChartBars == null || ChartPanel == null || RenderTarget == null)
				return;

			try
			{
				RebuildProfileIfNeeded();
				if (profileRows == null || profileRows.Count == 0 || maxVolume <= 0)
					return;

				float panelTop = ChartPanel.Y;
				float panelHeight = ChartPanel.Height;
				float profileWidth = Math.Max(30f, ChartPanel.Width * 0.22f);
				float xRight = ChartPanel.X + ChartPanel.Width - 2f;
				float rowHeight = panelHeight / Math.Max(1, profileRows.Count);

				for (int i = 0; i < profileRows.Count; i++)
				{
					ProfileRow row = profileRows[i];
					float yTop = panelTop + i * rowHeight;
					float yBottom = (i == profileRows.Count - 1) ? panelTop + panelHeight : yTop + rowHeight;
					float barWidth = (float)(profileWidth * (row.Volume / maxVolume));
					float xLeft = xRight - barWidth;
					var rect = new SharpDX.RectangleF(xLeft, yTop, barWidth, Math.Max(1f, yBottom - yTop - 1f));

					var dxBrush = GetRowBrush(row.Price);
					if (dxBrush != null)
						RenderTarget.FillRectangle(rect, dxBrush);

					if (borderDxBrush != null && BorderBrush != Brushes.Transparent)
						RenderTarget.DrawRectangle(rect, borderDxBrush, 1f);
				}
			}
			catch (Exception ex)
			{
				Log($"AdaptiveVolumeProfile render error: {ex.Message}", LogLevel.Error);
			}
		}

		protected override void OnRenderTargetChanged()
		{
			DisposeDxResources();
			if (RenderTarget == null)
				return;

			pocDxBrush = PocBrush.ToDxBrush(RenderTarget);
			valueAreaDxBrush = ValueAreaBrush.ToDxBrush(RenderTarget);
			outsideDxBrush = OutsideBrush.ToDxBrush(RenderTarget);
			borderDxBrush = BorderBrush.ToDxBrush(RenderTarget);
			textFormat = new TextFormat(Core.Globals.DirectWriteFactory, "Arial", 12f);
		}

		private void ResetSessionData()
		{
			if (volumeByPrice == null || profileRows == null)
				return;

			volumeByPrice.Clear();
			profileRows.Clear();
			pocPrice = double.NaN;
			valueAreaLow = double.NaN;
			valueAreaHigh = double.NaN;
			maxVolume = 0;
			totalVolume = 0;
			profileDirty = true;
		}

		private void RebuildProfileIfNeeded()
		{
			if (!profileDirty || volumeByPrice == null || volumeByPrice.Count == 0)
				return;

			profileRows.Clear();
			maxVolume = 0;
			totalVolume = 0;

			var prices = volumeByPrice.Keys.OrderBy(p => p).ToList();
			if (prices.Count == 0)
				return;

			double minPrice = prices.First();
			double maxPrice = prices.Last();
			double tick = TickSize;
			double rowStep = tick;
			int rowCount = Math.Max(1, Rows);
			double rangeTicks = Math.Max(1, Math.Round((maxPrice - minPrice) / tick) + 1);
			double ticksPerRow = Math.Max(1, Math.Ceiling(rangeTicks / rowCount));
			double bucketSize = ticksPerRow * tick;

			for (int i = 0; i < rowCount; i++)
			{
				double rowPrice = Math.Round((minPrice + i * bucketSize) / tick) * tick;
				double rowVolume = 0;

				for (double p = rowPrice; p < rowPrice + bucketSize; p += tick)
				{
					p = Math.Round(p / tick) * tick;
					if (volumeByPrice.TryGetValue(p, out double v))
						rowVolume += v;
				}

				profileRows.Add(new ProfileRow { Price = rowPrice, Volume = rowVolume });
				totalVolume += rowVolume;
				if (rowVolume > maxVolume)
				{
					maxVolume = rowVolume;
					pocPrice = rowPrice;
				}
			}

			CalculateValueArea();
			profileDirty = false;
		}

		private void CalculateValueArea()
		{
			if (profileRows == null || profileRows.Count == 0 || double.IsNaN(pocPrice))
				return;

			int pocIndex = profileRows.FindIndex(r => r.Price == pocPrice);
			if (pocIndex < 0)
				return;

			double target = totalVolume * (ValueAreaPercentage / 100.0);
			double accumulated = profileRows[pocIndex].Volume;
			int low = pocIndex;
			int high = pocIndex;

			while (accumulated < target && (low > 0 || high < profileRows.Count - 1))
			{
				double volumeAbove = high < profileRows.Count - 1 ? profileRows[high + 1].Volume : double.MinValue;
				double volumeBelow = low > 0 ? profileRows[low - 1].Volume : double.MinValue;

				if (volumeAbove >= volumeBelow)
				{
					if (high < profileRows.Count - 1)
					{
						high++;
						accumulated += profileRows[high].Volume;
					}
					else if (low > 0)
					{
						low--;
						accumulated += profileRows[low].Volume;
					}
				}
				else
				{
					if (low > 0)
					{
						low--;
						accumulated += profileRows[low].Volume;
					}
					else if (high < profileRows.Count - 1)
					{
						high++;
						accumulated += profileRows[high].Volume;
					}
				}
			}

			valueAreaLow = profileRows[low].Price;
			valueAreaHigh = profileRows[high].Price;
		}

		private SharpDX.Direct2D1.Brush GetRowBrush(double price)
		{
			if (!double.IsNaN(pocPrice) && Math.Abs(price - pocPrice) < TickSize * 0.5)
				return pocDxBrush;

			if (!double.IsNaN(valueAreaLow) && !double.IsNaN(valueAreaHigh) && price >= valueAreaLow && price <= valueAreaHigh)
				return valueAreaDxBrush;

			return outsideDxBrush;
		}

		private void DisposeDxResources()
		{
			if (pocDxBrush != null)
			{
				pocDxBrush.Dispose();
				pocDxBrush = null;
			}

			if (valueAreaDxBrush != null)
			{
				valueAreaDxBrush.Dispose();
				valueAreaDxBrush = null;
			}

			if (outsideDxBrush != null)
			{
				outsideDxBrush.Dispose();
				outsideDxBrush = null;
			}

			if (borderDxBrush != null)
			{
				borderDxBrush.Dispose();
				borderDxBrush = null;
			}

			if (textFormat != null)
			{
				textFormat.Dispose();
				textFormat = null;
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(5, 200)]
		[Display(Name = "Rows", Description = "Number of price levels in the profile", Order = 1, GroupName = "Parameters")]
		public int Rows { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Value Area %", Description = "Percentage of total volume used for the value area", Order = 2, GroupName = "Parameters")]
		public int ValueAreaPercentage { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Profile", Description = "Show or hide the rendered profile", Order = 3, GroupName = "Parameters")]
		public bool ShowProfile { get; set; }

		[XmlIgnore]
		[Display(Name = "POC Brush", Description = "Brush for the point of control", Order = 1, GroupName = "Colors")]
		public Brush PocBrush { get; set; }

		[Browsable(false)]
		public string PocBrushSerialize
		{
			get { return Serialize.BrushToString(PocBrush); }
			set { PocBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Value Area Brush", Description = "Brush for the value area", Order = 2, GroupName = "Colors")]
		public Brush ValueAreaBrush { get; set; }

		[Browsable(false)]
		public string ValueAreaBrushSerialize
		{
			get { return Serialize.BrushToString(ValueAreaBrush); }
			set { ValueAreaBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Outside Brush", Description = "Brush for levels outside value area", Order = 3, GroupName = "Colors")]
		public Brush OutsideBrush { get; set; }

		[Browsable(false)]
		public string OutsideBrushSerialize
		{
			get { return Serialize.BrushToString(OutsideBrush); }
			set { OutsideBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Border Brush", Description = "Brush for profile border", Order = 4, GroupName = "Colors")]
		public Brush BorderBrush { get; set; }

		[Browsable(false)]
		public string BorderBrushSerialize
		{
			get { return Serialize.BrushToString(BorderBrush); }
			set { BorderBrush = Serialize.StringToBrush(value); }
		}
		#endregion
	}
}
