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
	public class AdaptiveVolumeProfile_NT8_CF_Fast : Indicator
	{
		private SessionIterator sessionIterator;
		private DateTime previousSessionBegin;
		private Dictionary<double, long> volumeProfile;
		private double pocPrice;
		private double vah;
		private double val;
		private long maxVolume;
		private int rowHeight;

		// SharpDX resources
		private SolidColorBrush pocBrushDx;
		private SolidColorBrush vaBrushDx;
		private SolidColorBrush otherBrushDx;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Adaptive Volume Profile for current session only. Shows horizontal volume bars on right side with POC and Value Area.";
				Name										= "AdaptiveVolumeProfile_NT8_CF_Fast";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= true;
				DisplayInDataBox							= false;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= false;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive					= true;

				// Defaults
				NumberOfLevels								= 50;
				ValueAreaPercentage							= 70.0;
				ShowProfile									= true;
				MaxBarWidth									= 50.0;
				ProfileXOffset								= 10;

				// Brushes with defaults
				POCBrush									= Brushes.Red;
				ValueAreaBrush								= Brushes.Yellow;
				OtherBrush									= Brushes.LightGray;

				AddPlot(Brushes.Transparent, "Anchor");
			}
			else if (State == State.Configure)
			{
				// Nothing special here
			}
			else if (State == State.DataLoaded)
			{
				sessionIterator								= new SessionIterator(Bars);
				volumeProfile								= new Dictionary<double, long>();
				pocPrice									= double.NaN;
				vah											= double.NaN;
				val											= double.NaN;
				maxVolume									= 0;
				previousSessionBegin						= sessionIterator.ActualSessionBegin;
				rowHeight									= 2; // in pixels
			}
			else if (State == State.Terminated)
			{
				if (pocBrushDx != null && !pocBrushDx.IsDisposed)
					pocBrushDx.Dispose();
				if (vaBrushDx != null && !vaBrushDx.IsDisposed)
					vaBrushDx.Dispose();
				if (otherBrushDx != null && !otherBrushDx.IsDisposed)
					otherBrushDx.Dispose();
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 1)
				return;

			if (TickSize == 0)
				return; // Safety for some instruments

			// Session reset
			DateTime currentSessionBegin = sessionIterator.ActualSessionBegin;
			if (currentSessionBegin != previousSessionBegin)
			{
				volumeProfile.Clear();
				pocPrice = double.NaN;
				vah = double.NaN;
				val = double.NaN;
				maxVolume = 0;
				previousSessionBegin = currentSessionBegin;
			}

			// Add volume to price bucket
			if (double.IsNaN(Close[0]) || Volume[0] <= 0)
				return;

			// Bucket price levels
			double minPrice = volumeProfile.Count > 0 ? volumeProfile.Keys.Min() : Close[0];
			double maxPrice = volumeProfile.Count > 0 ? volumeProfile.Keys.Max() : Close[0];

			minPrice = Math.Min(minPrice, Close[0]);
			maxPrice = Math.Max(maxPrice, Close[0]);

			int numLevels = Math.Max(NumberOfLevels, 1);
			double priceRange = maxPrice - minPrice;
			if (priceRange <= 0)
				priceRange = TickSize;

			double bucketSize = priceRange / numLevels;
			if (bucketSize <= 0)
				bucketSize = TickSize;

			// Round Close to nearest bucket
			double bucketKey = Math.Round((Close[0] - minPrice) / bucketSize) * bucketSize + minPrice;

			if (volumeProfile.ContainsKey(bucketKey))
				volumeProfile[bucketKey] += Volume[0];
			else
				volumeProfile[bucketKey] = Volume[0];

			// Recalculate POC and VA
			RecalculatePOCAndVA();
		}

		private void RecalculatePOCAndVA()
		{
			if (volumeProfile.Count == 0)
			{
				pocPrice = double.NaN;
				vah = double.NaN;
				val = double.NaN;
				maxVolume = 0;
				return;
			}

			// Find POC
			var maxKV = volumeProfile.Aggregate((l, r) => l.Value > r.Value ? l : r);
			pocPrice = maxKV.Key;
			maxVolume = maxKV.Value;

			// VA: 70% around POC
			long totalVolume = volumeProfile.Values.Sum();
			long targetVol = (long)(totalVolume * ValueAreaPercentage / 100.0);
			long accumulatedVol = 0;

			// Sort prices
			var sortedPrices = volumeProfile.OrderBy(kv => Math.Abs(kv.Key - pocPrice)).ThenBy(kv => kv.Key).ToList();
			List<double> vaPrices = new List<double>();
			vaPrices.Add(pocPrice); // include POC
			accumulatedVol += maxVolume;

			// Add symmetric from POC
			foreach (var kv in sortedPrices.Where(kv => kv.Key != pocPrice).OrderBy(kv => Math.Abs(kv.Key - pocPrice)))
			{
				if (accumulatedVol >= targetVol)
					break;

				vaPrices.Add(kv.Key);
				accumulatedVol += kv.Value;
			}

			vah = vaPrices.Max();
			val = vaPrices.Min();
		}

		public override void OnRenderTargetChanged()
		{
			// Dispose existing
			if (pocBrushDx != null && !pocBrushDx.IsDisposed) pocBrushDx.Dispose();
			if (vaBrushDx != null && !vaBrushDx.IsDisposed) vaBrushDx.Dispose();
			if (otherBrushDx != null && !otherBrushDx.IsDisposed) otherBrushDx.Dispose();

			if (RenderTarget == null)
				return;

			// Create new brushes
			pocBrushDx = new SolidColorBrush(RenderTarget, POCBrush.ToDxColor4());
			vaBrushDx = new SolidColorBrush(RenderTarget, ValueAreaBrush.ToDxColor4());
			otherBrushDx = new SolidColorBrush(RenderTarget, OtherBrush.ToDxColor4());
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (!ShowProfile || RenderTarget == null || chartControl == null || chartScale == null ||
				ChartBars == null || volumeProfile.Count == 0 || double.IsNaN(pocPrice) ||
				ChartPanel == null)
			{
				return;
			}

			try
			{
				float xRight = (float)(ChartPanel.X + ChartPanel.W - ProfileXOffset);
				float maxWidth = (float)MaxBarWidth;

				// Sort levels by price ascending
				var sortedLevels = volumeProfile.OrderBy(kv => kv.Key).ToList();

				for (int i = 0; i < sortedLevels.Count; i++)
				{
					KeyValuePair<double, long> kv = sortedLevels[i];
					float yTop;

					if (i < sortedLevels.Count - 1)
					{
						float priceCurrent = (float)kv.Key;
						float priceNext = (float)sortedLevels[i + 1].Key;
						yTop = chartScale.GetYByValue(priceCurrent + (priceNext - priceCurrent) / 2.0);
					}
					else
					{
						yTop = chartScale.GetYByValue(kv.Key);
					}

					// Bar dimensions
					float height = rowHeight;
					float width = (float)((kv.Value / (double)maxVolume) * maxWidth);

					// Select brush
					SolidColorBrush useBrush;
					if (Math.Abs(kv.Key - pocPrice) < TickSize / 2.0)
						useBrush = pocBrushDx;
					else if (!double.IsNaN(val) && !double.IsNaN(vah) && kv.Key >= val && kv.Key <= vah)
						useBrush = vaBrushDx;
					else
						useBrush = otherBrushDx;

					// Draw bar
					if (width > 0 && height > 0 && useBrush != null)
					{
						var rect = new RectangleF(xRight - width, yTop - height / 2, width, height);
						RenderTarget.FillRectangle(rect, useBrush);
					}
				}
			}
			catch (Exception ex)
			{
				Print($"AdaptiveVolumeProfile Render Error: {ex.Message}");
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(10, 200)]
		[Display(Name = "Number of Price Levels", Order = 1, GroupName = "Parameters")]
		public int NumberOfLevels { get; set; }

		[NinjaScriptProperty]
		[Range(10, 100)]
		[Display(Name = "Value Area %", Order = 2, GroupName = "Parameters")]
		public double ValueAreaPercentage { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Profile", Order = 3, GroupName = "Parameters")]
		public bool ShowProfile { get; set; }

		[NinjaScriptProperty]
		[Range(10, 200)]
		[Display(Name = "Max Bar Width", Order =マーケティング 4, GroupName = "Parameters")]
		public double MaxBarWidth { get; set; }

		[NinjaScriptProperty]
		[Range(5, 50)]
		[Display(Name = "Profile X Offset", Order = 5, GroupName = "Parameters")]
		public int ProfileXOffset { get; set; }

		[XmlIgnore]
		[Display(Name = "POC Color", Order = 1, GroupName = "Colors")]
		public Brush POCBrush { get; set; }

		[Browsable(false)]
		public string POCBrushSerializable
		{
			get { return Serialize.BrushToString(POCBrush); }
			set { POCBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Value Area Color", Order = 2, GroupName = "Colors")]
		public Brush ValueAreaBrush { get; set; }

		[Browsable(false)]
		public string ValueAreaBrushSerializable
		{
			get { return Serialize.BrushToString(ValueAreaBrush); }
			set { ValueAreaBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Other Levels Color", Order = 3, GroupName = "Colors")]
		public Brush OtherBrush { get; set; }

		[Browsable(false)]
		public string OtherBrushSerializable
		{
			get { return Serialize.BrushToString(OtherBrush); }
			set { OtherBrush = Serialize.StringToBrush(value); }
		}
		#endregion
	}
}