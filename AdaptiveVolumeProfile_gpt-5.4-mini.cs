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
		private Dictionary<double, double> volumeByPrice;
		private List<ProfileRow> profileRows;
		private bool profileDirty;
		private bool resourcesInitialized;
		private double sessionHigh;
		private double sessionLow;
		private double pocPrice;
		private double valueAreaLow;
		private double valueAreaHigh;
		private double maxVolume;
		private double totalVolume;
		private int pocIndex;
		private int valueAreaLowIndex;
		private int valueAreaHighIndex;
		private SharpDX.Direct2D1.Brush pocDxBrush;
		private SharpDX.Direct2D1.Brush valueAreaDxBrush;
		private SharpDX.Direct2D1.Brush outsideDxBrush;
		private SharpDX.Direct2D1.Brush borderDxBrush;
		private TextFormat textFormat;
		private bool sessionInitialized;

		private struct ProfileRow
		{
			public double Price;
			public double Volume;
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description				= @"Real-time session volume profile rendered with SharpDX.";
				Name					= "AdaptiveVolumeProfile";
				Calculate				= Calculate.OnEachTick;
				IsOverlay				= true;
				DisplayInDataBox		= false;
				DrawOnPricePanel		= true;
				DrawHorizontalGridLines = false;
				DrawVerticalGridLines	= false;
				PaintPriceMarkers		= false;
				ScaleJustification		= ScaleJustification.Right;
				IsSuspendedWhileInactive = true;
				Rows					= 48;
				ShowProfile				= true;
				PocBrush				= Brushes.Gold;
				ValueAreaBrush			= Brushes.DodgerBlue;
				OutsideBrush			= Brushes.DimGray;
				BorderBrush				= Brushes.Transparent;
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

			if (Bars.IsFirstBarOfSession && !sessionInitialized)
			{
				ResetSessionData();
				sessionInitialized = true;
			}
			else if (Bars.IsFirstBarOfSession && CurrentBar > 0)
			{
				ResetSessionData();
			}

			if (Volume[0] <= 0)
				return;

			double price = Instrument.MasterInstrument.RoundToTickSize(Close[0]);
			if (!volumeByPrice.ContainsKey(price))
				volumeByPrice[price] = 0;
			volumeByPrice[price] += Volume[0];
			profileDirty = true;
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);

			if (!ShowProfile || chartControl == null || chartScale == null || ChartBars == null || ChartPanel == null || RenderTarget == null)
				return;

			if (ChartBars.FromIndex < 0 || ChartBars.ToIndex < 0)
				return;

			try
			{
				RebuildProfileIfNeeded();

				if (profileRows == null || profileRows.Count == 0 || maxVolume <= 0)
					return;

				float panelRight = ChartPanel.X + ChartPanel.Width;
				float panelTop = ChartPanel.Y;
				float panelHeight = ChartPanel.Height;
				float profileWidth = Math.Max(30f, ChartPanel.Width * 0.22f);
				float xRight = panelRight - 2f;
				float rowHeight = (float)(panelHeight / profileRows.Count);

				for (int i = 0; i < profileRows.Count; i++)
				{
					ProfileRow row = profileRows[i];
					float yTop = panelTop + i * rowHeight;
					float yBottom = (i == profileRows.Count - 1) ? panelTop + panelHeight : yTop + rowHeight;
					float barWidth = (float)(profileWidth * (row.Volume / maxVolume));
					float xLeft = xRight - barWidth;
					RectangleF rect = new RectangleF(xLeft, yTop, barWidth, Math.Max(1f, yBottom - yTop - 1f));

					SharpDX.Direct2D1.Brush dxBrush = GetRowBrush(row.Price);
					if (dxBrush != null)
						RenderTarget.FillRectangle(rect, dxBrush);

					if (BorderBrush != Brushes.Transparent && borderDxBrush != null)
						RenderTarget.DrawRectangle(rect, borderDxBrush, 1f);
				}
			}
			catch (Exception)
			{
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
			resourcesInitialized = true;
		}

		private void ResetSessionData()
		{
			if (volumeByPrice == null)
				return;

			volumeByPrice.Clear();
			profileRows.Clear();
			profileDirty = true;
			pocPrice = 0;
			valueAreaLow = 0;
			valueAreaHigh = 0;
			maxVolume = 0;
			totalVolume = 0;
			pocIndex = -1;
			valueAreaLowIndex = -1;
			valueAreaHighIndex = -1;
		}

		private void RebuildProfileIfNeeded()
		{
			if (!profileDirty || volumeByPrice == null || volumeByPrice.Count == 0)
				return;

			profileRows.Clear();
			maxVolume = 0;
			totalVolume = 0;

			var ordered = volumeByPrice.Keys.OrderBy(x => x).ToList();
			if (ordered.Count == 0)
				return;

			double minPrice = ordered.First();
			double maxPrice = ordered.Last();
			double span = Math.Max(TickSize, maxPrice - minPrice);
			double step = Math.Max(TickSize, Instrument.MasterInstrument.RoundToTickSize(span / Math.Max(1, Rows - 1)));

			for (int i = 0; i < Rows; i++)
			{
				double price = Instrument.MasterInstrument.RoundToTickSize(minPrice + i * step);
				double vol = 0;
				if (volumeByPrice.TryGetValue(price, out double exactVol))
					vol = exactVol;
				else
				{
					var nearest = ordered.OrderBy(p => Math.Abs(p - price)).FirstOrDefault();
					if (nearest != 0 || volumeByPrice.ContainsKey(nearest))
						vol = volumeByPrice[nearest];
				}

				profileRows.Add(new ProfileRow { Price = price, Volume = vol });
				totalVolume += vol;
				if (vol > maxVolume)
				{
					maxVolume = vol;
					pocPrice = price;
					pocIndex = i;
				}
			}

			CalculateValueArea();
			profileDirty = false;
		}

		private void CalculateValueArea()
		{
			if (profileRows == null || profileRows.Count == 0 || pocIndex < 0)
				return;

			double target = totalVolume * 0.70;
			double cum = profileRows[pocIndex].Volume;
			int low = pocIndex;
			int high = pocIndex;

			while (cum < target && (low > 0 || high < profileRows.Count - 1))
			{
				double below = low > 0 ? profileRows[low - 1].Volume : double.MinValue;
				double above = high < profileRows.Count - 1 ? profileRows[high + 1].Volume : double.MinValue;

				if (above >= below)
				{
					if (high < profileRows.Count - 1)
					{
						high++;
						cum += profileRows[high].Volume;
					}
					else if (low > 0)
					{
						low--;
						cum += profileRows[low].Volume;
					}
				}
				else
				{
					if (low > 0)
					{
						low--;
						cum += profileRows[low].Volume;
					}
					else if (high < profileRows.Count - 1)
					{
						high++;
						cum += profileRows[high].Volume;
					}
				}
			}

			valueAreaLowIndex = low;
			valueAreaHighIndex = high;
			valueAreaLow = profileRows[low].Price;
			valueAreaHigh = profileRows[high].Price;
		}

		private SharpDX.Direct2D1.Brush GetRowBrush(double price)
		{
			if (Math.Abs(price - pocPrice) < TickSize * 0.5)
				return pocDxBrush;

			if (price >= valueAreaLow && price <= valueAreaHigh)
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

			resourcesInitialized = false;
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(5, 200)]
		[Display(Name = "Rows", Description = "Number of price levels in the profile", Order = 1, GroupName = "Parameters")]
		public int Rows { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Profile", Description = "Show or hide the rendered profile", Order = 2, GroupName = "Parameters")]
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
