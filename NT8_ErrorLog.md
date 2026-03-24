========================================
NT8 ERROR LOG - LIVING DOCUMENT
========================================
Purpose: Record every bug discovered during testing with fix.
Update this file whenever a new error is found and solved.
Upload updated version to Chroma after every update.

========================================
ERROR LOG ENTRIES
========================================

DATE: March 2026
SOURCE: AdaptiveVolumeProfile testing - Multiple models
----------------------------------------
ERROR 1:
Issue: .Freeze() called on SharpDX SolidColorBrush
Symptom: Compile error - method does not exist
Fix: Remove .Freeze() entirely. SharpDX brushes do not support Freeze()
Models affected: Grok Code Fast 1, early iterations

ERROR 2:
Issue: Context.GetDataSeries() used instead of AddDataSeries()
Symptom: Compile error - Context does not contain GetDataSeries
Fix: Use AddDataSeries(BarsPeriodType.Day, 1) in State.Configure
Models affected: Grok Code Fast 1

ERROR 3:
Issue: BarsArray[x].GetHigh(n) used for secondary series access
Symptom: Compile error - incorrect method
Fix: Use Highs[1][n] and Lows[1][n] syntax
Models affected: Grok Code Fast 1, Grok 4.1 Fast

ERROR 4:
Issue: Alert() called with System.Drawing.Color instead of WPF Brushes
Symptom: Compile error - wrong parameter type
Fix: Use Brushes.Red, Brushes.Green etc instead of System.Drawing.Color
Models affected: Grok Code Fast 1

ERROR 5:
Issue: OnRenderTargetChanged placed inside OnStateChange as a state
Symptom: Brushes never initialized, zones never render
Fix: OnRenderTargetChanged must be a standalone protected override method
Models affected: Grok 4.1 Fast

ERROR 6:
Issue: ChartPanel.W used instead of ChartPanel.Width
Symptom: Compile error - W property does not exist
Fix: Always use ChartPanel.Width
Models affected: Grok Code Fast 1, DeepSeek V3.2, Grok 4.1 Fast

ERROR 7:
Issue: IsChartOnly set as property in SetDefaults
Symptom: Compile error - property does not exist in NT8
Fix: Remove IsChartOnly entirely, not a valid NT8 property
Models affected: DeepSeek V3.2, Grok Code Fast 1

ERROR 8:
Issue: Time[0] accessed inside ResetSessionData() called from 
State.DataLoaded
Symptom: Runtime exception on indicator load
Fix: Move session reset call to OnBarUpdate with IsFirstBarOfSession check
Models affected: DeepSeek V3.2, Grok Code Fast 1

ERROR 9:
Issue: Dynamic bucket size recalculated every bar
Symptom: Volume assigned to wrong price levels as session progresses,
profile shows incorrect distribution
Fix: Always normalize price to TickSize for stable fixed buckets
Models affected: Grok Code Fast 1

ERROR 10:
Issue: Value Area calculated by sorting prices by distance from POC
Symptom: Incorrect Value Area boundaries, VAH/VAL wrong
Fix: Use price-level expansion algorithm comparing adjacent volume
above and below current boundaries
Models affected: DeepSeek V3.2, Grok Code Fast 1, Grok 4.1 Fast

ERROR 11:
Issue: public override OnRenderTargetChanged() instead of protected
Symptom: Compile warning or error depending on NT8 version
Fix: Always use protected override void OnRenderTargetChanged()
Models affected: Grok Code Fast 1

ERROR 12:
Issue: .ToDxColor4() extension method used on WPF Brush
Symptom: Compile error - method does not exist
Fix: Use .ToDxBrush(RenderTarget) extension method instead
Models affected: Grok Code Fast 1

ERROR 13:
Issue: Calculate.OnBarClose used for volume profile indicator
Symptom: Indicator only updates once per bar, misses intra-bar 
volume distribution
Fix: Use Calculate.OnEachTick for volume profiles
Models affected: Grok Code Fast 1, DeepSeek V3.2

ERROR 14:
Issue: Foreign characters embedded in Display attribute Order field
Example: Order =マーケティング 4
Symptom: Compile error
Fix: Always use English integers only in Display attributes
Models affected: Grok Code Fast 1

ERROR 15:
Issue: Private variables initialized inline at declaration AND 
again in State.DataLoaded
Example: private Dictionary<double, long> volumeProfile = new Dictionary<double, long>();
Symptom: Redundant initialization, potential unexpected behavior
Fix: Declare without initialization at top, initialize only in 
State.DataLoaded
WRONG: private Dictionary<double, long> volumeProfile = new Dictionary<double, long>();
CORRECT: private Dictionary<double, long> volumeProfile;
Then in State.DataLoaded: volumeProfile = new Dictionary<double, long>();
Models affected: Grok Code Fast 1

ERROR 16:
Issue: Volume accumulation split between OnMarketData and 
OnBarUpdate causes double counting
Symptom: Volume profile shows inflated values, inaccurate distribution
Fix: Never split volume logic between OnMarketData and OnBarUpdate
Pick one approach only:
Option 1 - OnMarketData only (preferred for tick accuracy):
Accumulate in OnMarketData, handle session reset with a flag
Option 2 - OnBarUpdate only:
Use Calculate.OnEachTick, accumulate from Volume[0] each tick
Never use both simultaneously
Models affected: Grok Code Fast 1

ERROR 17:
Issue: Value Area algorithm regressed to split-volume approach
Symptom: VAH and VAL calculated incorrectly, splits total volume 
in half from each end instead of expanding from POC outward
Fix: Always use price-level expansion algorithm from POC outward
comparing volume above vs below at each step
Models affected: Grok Code Fast 1 v2

ERROR 18:
Issue: ToDxColor4() and brush creation inside OnRender using blocks 
both regressed in v2 despite being fixed in v1
Symptom: Compile error on ToDxColor4, performance issue on brush creation
Fix: Always use ToDxBrush(RenderTarget), always create brushes in 
OnRenderTargetChanged never inside OnRender
Models affected: Grok Code Fast 1 v2, GPT-5.4 Mini

ERROR 18 UPDATE:
Even when brushes are already created in OnRenderTargetChanged 
as class-level variables, models still create additional 
temporary brushes inside OnRender using blocks for color 
selection logic. This is wrong.
WRONG pattern seen in GPT-5.4 Mini:
using (var fill = new SolidColorBrush(RenderTarget, ToColor4(chosen)))
    RenderTarget.FillRectangle(rect, fill);
CORRECT pattern:
Use the pre-created class-level brushes directly:
SolidColorBrush brush = (isPOC) ? pocDxBrush : 
    (isValueArea) ? valueAreaDxBrush : outsideDxBrush;
RenderTarget.FillRectangle(rect, brush);
Never create new brush objects inside OnRender under any 
circumstances regardless of how color selection is handled.
Models affected: Grok Code Fast 1 v2, GPT-5.4 Mini

ERROR 19:
Issue: ValueAreaPercentage hardcoded as 0.70 instead of 
configurable property
Symptom: User cannot adjust Value Area percentage from UI
Fix: Always expose ValueAreaPercentage as a NinjaScriptProperty 
with Range(1, 100) defaulting to 70 in State.SetDefaults
Models affected: GPT-5.4 Mini, Grok Code Fast 1 multiple versions

ERROR 20:
Issue: Dynamic bucket step size calculated as span/Rows 
causes volume to shift between buckets as session progresses
Symptom: Volume profile shows inaccurate distribution
Fix: Always normalize price to TickSize using 
Math.Round(price / TickSize) * TickSize
Never calculate dynamic step sizes based on price range
Models affected: GPT-5.4 Mini, Grok Code Fast 1 multiple versions

ERROR 20 UPDATE:
Dynamic bucketing persisted even after fix attempt.
Model correctly normalized price on input but still 
groups ticks into dynamic buckets during profile rebuild.
True fix: store and display raw TickSize normalized prices 
directly without any grouping logic.

ERROR 21:
Issue: Incomplete volume profile implementation that attempts
to use volume from bars in OnMarketData context
Symptom: Volume[0] does not update in OnMarketData, leading to
incorrect volume accumulation
Fix: Properly separate logic between OnMarketData for individual 
tick data and OnBarUpdate for session resetting
Never use Volume[0] in OnMarketData, only use MarketDataEventArgs
Models affected: Qwen3 Coder Plus initial implementation

========================================
NEW ERRORS - March 2026 Session
========================================

ERROR 22:
Issue: Missing #region Using declarations wrapper around using statements
Symptom: Code style inconsistency, potential organization issues
Fix: Always wrap using declarations in a #region block:
#region Using declarations
using System;
using System.Collections.Generic;
// etc
#endregion
Models affected: AdaptiveVolumeProfile MiniMax v1

ERROR 23:
Issue: DrawOnPricePanel = false for overlay indicator
Symptom: Indicator renders incorrectly or not at all in price panel
Fix: Set DrawOnPricePanel = true when IsOverlay = true
This is required for indicators that draw on the price chart
Models affected: AdaptiveVolumeProfile MiniMax v1

ERROR 24:
Issue: Value Area algorithm still using distance-based sorting
Example: var sortedByDistance = volumeProfile.OrderBy(kv => Math.Abs(kv.Key - pocPrice));
Symptom: Value Area expands symmetrically from POC regardless of 
volume distribution, producing incorrect boundaries
Fix: Use proper volume-weighted expansion algorithm:
- Separate levels above and below POC
- At each step, compare remaining volume above vs below
- Expand toward the side with HIGHER remaining volume
- Continue until target percentage is reached
CORRECT algorithm: Track indices separately for above and below,
compare cumulative volume at each expansion step, expand toward
side with more volume first
Models affected: AdaptiveVolumeProfile MiniMax v1

ERROR 25:
Issue: Variable declared inside if block used outside its scope
Example:
if (condition)
{
    double belowPrice = 123.45;
}
else if (!valueAreaPrices.Contains(belowPrice)) // belowPrice not in scope here
Symptom: Compile error - name does not exist in current context
Fix: Declare variables at method scope, not inside conditional blocks
if you need them in multiple branches or after the block
Models affected: AdaptiveVolumeProfile MiniMax v1

ERROR 26:
Issue: Values[0], Values[1], Values[2] exposed but AddPlot() never 
called in SetDefaults
Symptom: Indicator may not compile or produces unexpected behavior
Fix: Either remove the Values[] references entirely OR call AddPlot()
in State.SetDefaults if plots are intended
For pure rendering indicators (no data series output), remove Values[] refs
Models affected: AdaptiveVolumeProfile MiniMax v1

ERROR 27:
Issue: Namespace not properly set for NT8 indicator
Symptom: Indicator doesn't compile or doesn't appear in NT8 indicator list
Fix: Use namespace NinjaTrader.NinjaScript.Indicators
WRONG: namespace MyCustomName { ... }
CORRECT: namespace NinjaTrader.NinjaScript.Indicators { ... }
Models affected: AdaptiveVolumeProfile MiniMax v1

ERROR 28:
Issue: Using wrong namespace prefix causing System.Drawing references
Symptom: Indicator uses System.Windows.Media.Brush instead of SharpDX
for rendering, causing conflicts or incorrect rendering
Fix: Use SharpDX.Direct2D1.SolidColorBrush for all rendering
Never use System.Drawing namespace for NT8 indicators
Models affected: AdaptiveVolumeProfile MiniMax v1

ERROR 29:
Issue: ShowProfile toggle not working - rendering executes even when
ShowProfile = false
Symptom: Profile displays regardless of ShowProfile setting
Fix: Add early return in OnRender if !ShowProfile
Must be the FIRST check in OnRender, before any other validation
Models affected: AdaptiveVolumeProfile MiniMax v1

ERROR 30:
Issue: Value Area algorithm compares TOTAL remaining volume above vs 
below instead of comparing NEXT adjacent level volume
Symptom: Value Area expands incorrectly - may include small-volume 
levels when large-volume levels exist on other side
Fix: At each expansion step, compare ONLY the next adjacent level 
on each side, not the total remaining:
CORRECT:
double nextAboveVol = aboveIndex < pricesAbove.Count ? 
    pricesAbove[aboveIndex].Value : double.MaxValue;
double nextBelowVol = belowIndex < pricesBelow.Count ? 
    pricesBelow[belowIndex].Value : double.MaxValue;
addAbove = nextAboveVol >= nextBelowVol;  // Compare NEXT level only
WRONG:
addAbove = aboveVolume >= belowVolume;  // Compares TOTAL remaining
Models affected: AdaptiveVolumeProfile MiniMax v1

ERROR 31:
Issue: Volume accumulating cumulatively instead of as delta per bar
Symptom: Volume at each price level is inflated because Volume[0] 
(bar's total volume) is added on each tick instead of incremental
Fix: Track lastBarVolume and compute delta = Volume[0] - lastBarVolume
Add only the delta to price levels, then update lastBarVolume
NEVER add Volume[0] directly - it contains total bar volume not delta
Models affected: AdaptiveVolumeProfile MiniMax v1

========================================
END OF ERROR LOG
========================================
