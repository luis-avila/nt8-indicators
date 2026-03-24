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
#endregion

// This namespace holds indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
{
	public class AdaptiveVolumeProfile : Indicator
	{
		private Dictionary<double, long> volumeProfile;
		private SessionIterator sessionIterator;
		private DateTime previousSessionBegin;
		
		// Calculated values
		private double pocPrice;			// Point of Control - highest volume price level
		private double vah;				// Value Area High
		private double val;				// Value Area Low
		private long maxVolume;			// Highest volume at any price level (for proportional sizing)
		
		// SharpDX brush variables for rendering
		private SolidColorBrush pocBrushDx;
		private SolidColorBrush valueAreaBrushDx;
		private SolidColorBrush otherBrushDx;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Real-time volume profile that displays as horizontal bars on the right side of the price panel. Automatically resets at each new session.";
				Name										= "AdaptiveVolumeProfile";
				Calculate									= Calculate.OnBarUpdate;
				IsOverlay									= true;
				DisplayInDataBox							= false;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= false;
				DrawVerticalGridLines						= false;
				PaintPriceMarkers							= false;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive					= false;
				
				// Default properties
				ShowProfile								= true;
				NumberOfLevels							= 20;
				ValueAreaPercentage						= 70;
				ProfileXOffset							= 15;
				MaxBarWidth								= 80;
				RowHeight								= 2;
				
				// Color settings
				POCBrushColor							= Brushes.Gold;
				ValueAreaBrushColor						= Brushes.Orange;
				OtherBrushColor							= Brushes.Gray;
			}
			else if (State == State.Configure)
			{
				// Add any configuration code here
			}
			else if (State == State.DataLoaded)
			{
				volumeProfile = new Dictionary<double, long>();
				sessionIterator = new SessionIterator(Bars);
				previousSessionBegin = sessionIterator.ActualSessionBegin;
				
				// Ensure drawing tools are created on the chart
				pocBrushDx = null;
				valueAreaBrushDx = null;
				otherBrushDx = null;
			}
			else if (State == State.Terminated)
			{
				// Dispose of SharpDX brushes to prevent memory leaks
				if (pocBrushDx != null && !pocBrushDx.IsDisposed)
					pocBrushDx.Dispose();
				if (valueAreaBrushDx != null && !valueAreaBrushDx.IsDisposed)
					valueAreaBrushDx.Dispose();
				if (otherBrushDx != null && !otherBrushDx.IsDisposed)
					otherBrushDx.Dispose();
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 0) return;
			
			// Check if a new session has started and reset the profile if needed
			DateTime currentSessionBegin = sessionIterator.ActualSessionBegin;
			if (Bars.IsFirstBarOfSession) // This is the reliable way to detect session changes
			{
				volumeProfile.Clear();
				pocPrice = 0;
				vah = 0;
				val = 0;
				maxVolume = 0;
			}
			
			// Only process if we have volume data
			if (Volume[0] > 0)
			{
				// Normalize price to ticksize to create fixed price buckets
				double normalizedPrice = Math.Round(Input[0].Close / TickSize) * TickSize;
				
				// Add volume to the appropriate price level
				if (volumeProfile.ContainsKey(normalizedPrice))
					volumeProfile[normalizedPrice] += Volume[0];
				else
					volumeProfile[normalizedPrice] = Volume[0];
				
				// Recalculate POC and Value Area
				RecalculatePOCAndValueArea();
			}
		}
		
		void RecalculatePOCAndValueArea()
		{
			if (volumeProfile.Count == 0) return;
			
			// Find Point of Control (POC) - price level with highest volume
			var maxKV = volumeProfile.Aggregate((l, r) => l.Value > r.Value ? l : r);
			pocPrice = maxKV.Key;
			maxVolume = maxKV.Value;
			
			// Calculate total volume for this session
			long totalVolume = volumeProfile.Values.Sum();
			
			// Calculate value area boundaries that encompass the specified percentage of total volume
			double targetPercentage = ValueAreaPercentage / 100.0;
			long targetVol = (long)(totalVolume * targetPercentage);
			
			// Start from POC and expand outward based on volume
			var sortedByDistanceFromPOC = volumeProfile
				.OrderBy(kv => Math.Abs(kv.Key - pocPrice))
				.ThenBy(kv => kv.Key)
				.ToList();
			
			// Include the POC initially
			long accumulatedVol = volumeProfile[pocPrice];
			List<double> valueAreaPrices = new List<double> { pocPrice };
			
			// Expand outwards from POC until we reach the target volume
			foreach (var kv in sortedByDistanceFromPOC.Where(kv => kv.Key != pocPrice))
			{
				if (accumulatedVol >= targetVol) break;
				valueAreaPrices.Add(kv.Key);
				accumulatedVol += kv.Value;
			}
			
			// Set Value Area High and Low
			if (valueAreaPrices.Count > 0)
			{
				vah = valueAreaPrices.Max();
				val = valueAreaPrices.Min();
			}
		}

		public override void OnRenderTargetChanged()
		{
			// Create or recreate SharpDX brushes when the rendering target changes
			if (pocBrushDx != null && !pocBrushDx.IsDisposed)
				pocBrushDx.Dispose();
			if (valueAreaBrushDx != null && !valueAreaBrushDx.IsDisposed)
				valueAreaBrushDx.Dispose();
			if (otherBrushDx != null && !otherBrushDx.IsDisposed)
				otherBrushDx.Dispose();

			// Create brushes for each category
			pocBrushDx = new SolidColorBrush(RenderTarget, POCBrushColor.ToDxColor4());
			valueAreaBrushDx = new SolidColorBrush(RenderTarget, ValueAreaBrushColor.ToDxColor4());
			otherBrushDx = new SolidColorBrush(RenderTarget, OtherBrushColor.ToDxColor4());
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (!ShowProfile || volumeProfile.Count == 0) 
				return;

			float xRight = (float)(ChartPanel.X + ChartPanel.Width - ProfileXOffset);  // Use ChartPanel.Width, not ChartPanel.W
			
			// Use a consistent row height based on pixels
			float rowHeight = RowHeight;
			
			// Sort profile levels by price for proper rendering order
			var sortedLevels = volumeProfile.OrderBy(kv => kv.Key).ToList();
			
			for (int i = 0; i < sortedLevels.Count; i++)
			{
				var kv = sortedLevels[i];
				
				// Convert price level to Y coordinate on the chart
				float yTop = chartScale.GetYByValue(kv.Key);

				// Calculate bar width proportional to this level's volume compared to the highest volume (POC)
				double widthRatio = (double)kv.Value / (double)maxVolume;
				float maxBarWidth = MaxBarWidth;  // User-defined maximum bar width
				float width = (float)(widthRatio * maxBarWidth);
				
				// Determine which brush to use based on value area classification
				SolidColorBrush useBrush;
				if (Math.Abs(kv.Key - pocPrice) < (TickSize / 2))  // Check if this is the POC level
					useBrush = pocBrushDx;
				else if (kv.Key >= val && kv.Key <= vah)  // Check if within value area
					useBrush = valueAreaBrushDx;
				else  // Outside value area
					useBrush = otherBrushDx;
				
				// Create the rectangle for this level
				var rect = new RectangleF(
					xRight - width,    // Left edge
					yTop - rowHeight / 2,  // Top edge
					width,             // Width
					rowHeight          // Height
				);
				
				// Draw the rectangle using the appropriate brush
				RenderTarget.FillRectangle(rect, useBrush);
			}
		}

		#region Properties
		
		[NinjaScriptProperty]
		[Display(Name="Show Profile", Order=1, GroupName="Settings")]
		public bool ShowProfile { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name="Number Of Levels", Order=2, GroupName="Settings")]
		public int NumberOfLevels { get; set; }
		
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name="Value Area Percentage", Order=3, GroupName="Settings")]
		public int ValueAreaPercentage { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Profile X Offset", Order=4, GroupName="Settings")]
		public int ProfileXOffset { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Max Bar Width", Order=5, GroupName="Settings")]
		public int MaxBarWidth { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Row Height", Order=6, GroupName="Settings")]
		public float RowHeight { get; set; }
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="POC Color", Order=7, GroupName="Colors")]
		public System.Windows.Media.Brush POCBrushColor { get; set; }

		[Browsable(false)]
		public string POCBrushColorSerializable
		{
			get { return Serialize.BrushToString(POCBrushColor); }
			set { POCBrushColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="Value Area Color", Order=8, GroupName="Colors")]
		public System.Windows.Media.Brush ValueAreaBrushColor { get; set; }

		[Browsable(false)]
		public string ValueAreaBrushColorSerializable
		{
			get { return Serialize.BrushToString(ValueAreaBrushColor); }
			set { ValueAreaBrushColor = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="Other Color", Order=9, GroupName="Colors")]
		public System.Windows.Media.Brush OtherBrushColor { get; set; }

		[Browsable(false)]
		public string OtherBrushColorSerializable
		{
			get { return Serialize.BrushToString(OtherBrushColor); }
			set { OtherBrushColor = Serialize.StringToBrush(value); }
		}

		#endregion
	}
}

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.NinjaScript.IndicatorBase
	{
		private NinjaTrader.NinjaScript.Indicators.AdaptiveVolumeProfile adaptiveVolumeProfile;
		public NinjaTrader.NinjaScript.Indicators.AdaptiveVolumeProfile AdaptiveVolumeProfile(bool showProfile, int numberOfLevels, int valueAreaPercentage, int profileXOffset, int maxBarWidth, float rowHeight, System.Windows.Media.Brush pOCBrushColor, System.Windows.Media.Brush valueAreaBrushColor, System.Windows.Media.Brush otherBrushColor)
		{
			return adaptiveVolumeProfile ?? (adaptiveVolumeProfile = ManageIndicator.Create<NinjaTrader.NinjaScript.Indicators.AdaptiveVolumeProfile>(/*Instrumented=*/ true, 
				showProfile, 
				numberOfLevels, 
				valueAreaPercentage, 
				profileXOffset, 
				maxBarWidth, 
				rowHeight, 
				pOCBrushColor, 
				valueAreaBrushColor, 
				otherBrushColor));
		}
	}
}