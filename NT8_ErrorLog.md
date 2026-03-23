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

ERROR 17 — Value Area regression to split-volume approach
ERROR 18 — ToDxColor4 and brush creation regressions in v2

========================================
END OF ERROR LOG
========================================
