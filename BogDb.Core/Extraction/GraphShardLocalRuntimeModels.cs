namespace BogDb.Core.Extraction;

public enum GraphShardPredicateOperator
{
    Equal,
    NotEqual,
    In,
    NotIn,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Contains,
    StartsWith,
    Exists
}

public sealed record class GraphShardNodePredicateSpec
{
    public string? TableName { get; init; }
    public IReadOnlyList<string> TableNames { get; init; } = [];
    public IReadOnlyList<string> ExternalIds { get; init; } = [];
    public string PropertyName { get; init; } = string.Empty;
    public GraphShardPredicateOperator Operator { get; init; } = GraphShardPredicateOperator.Equal;
    public object? Value { get; init; }
    public IReadOnlyList<object?> Values { get; init; } = [];
    public bool CaseInsensitive { get; init; }
}

public sealed record class GraphShardNodeMatch
{
    public string TableName { get; init; } = string.Empty;
    public NodeShardRow Row { get; init; } = new();
}

public enum GraphShardFilterComposition
{
    All,
    Any
}

public sealed record class GraphShardNodeFilterSpec
{
    public GraphShardNodePredicateSpec? Predicate { get; init; }
    public GraphShardFilterComposition Composition { get; init; } = GraphShardFilterComposition.All;
    public IReadOnlyList<GraphShardNodeFilterSpec> Children { get; init; } = [];
    public GraphShardNodeFilterSpec? Not { get; init; }
}

public sealed record class GraphShardPathOptions
{
    public int? MaxDepth { get; init; }
    public bool IncludeOutgoing { get; init; } = true;
    public bool IncludeIncoming { get; init; }
    public string? RelType { get; init; }
}

public sealed record class GraphShardPathResult
{
    public List<string> NodeIds { get; init; } = [];
    public List<string> RelIds { get; init; } = [];
    public int Length => RelIds.Count;
}

public sealed record class GraphShardProjectedPath
{
    public GraphShardPathResult Path { get; init; } = new();
    public GraphShard Shard { get; init; } = new();
    public GraphShardNodeMatch StartNode { get; init; } = new();
    public GraphShardNodeMatch EndNode { get; init; } = new();
    public List<GraphShardNodeMatch> Nodes { get; init; } = [];
    public List<GraphShardRelationshipMatch> Relationships { get; init; } = [];
    public List<string> NodeTableSequence { get; init; } = [];
    public List<string> RelTypeSequence { get; init; } = [];
}

public sealed record class GraphShardRelationshipMatch
{
    public string RelType { get; init; } = string.Empty;
    public RelShardRow Row { get; init; } = new();
}

public sealed record class GraphShardRelationshipPredicateSpec
{
    public string? RelType { get; init; }
    public IReadOnlyList<string> RelTypes { get; init; } = [];
    public IReadOnlyList<string> RelIds { get; init; } = [];
    public IReadOnlyList<string> SourceNodeIds { get; init; } = [];
    public IReadOnlyList<string> TargetNodeIds { get; init; } = [];
    public string PropertyName { get; init; } = string.Empty;
    public GraphShardPredicateOperator Operator { get; init; } = GraphShardPredicateOperator.Equal;
    public object? Value { get; init; }
    public IReadOnlyList<object?> Values { get; init; } = [];
    public bool CaseInsensitive { get; init; }
}

public sealed record class GraphShardRelationshipFilterSpec
{
    public GraphShardRelationshipPredicateSpec? Predicate { get; init; }
    public GraphShardFilterComposition Composition { get; init; } = GraphShardFilterComposition.All;
    public IReadOnlyList<GraphShardRelationshipFilterSpec> Children { get; init; } = [];
    public GraphShardRelationshipFilterSpec? Not { get; init; }
}

public enum GraphShardAggregateFunction
{
    Count,
    Sum
}

public enum GraphShardAggregateSource
{
    Node,
    Relationship
}

public sealed record class GraphShardAggregateSpec
{
    public string Key { get; init; } = string.Empty;
    public GraphShardAggregateFunction Function { get; init; } = GraphShardAggregateFunction.Count;
    public string? PropertyName { get; init; }
    public GraphShardAggregateSource Source { get; init; } = GraphShardAggregateSource.Node;
}

public sealed record class GraphShardAggregateRow
{
    public string GroupKey { get; init; } = string.Empty;
    public Dictionary<string, object?> Values { get; init; } = new(StringComparer.Ordinal);
}

public sealed record class GraphShardAggregateResult
{
    public List<GraphShardAggregateRow> Rows { get; init; } = [];
}

public enum GraphShardSortDirection
{
    Asc,
    Desc
}

public sealed record class GraphShardNodeSortSpec
{
    public string? PropertyName { get; init; }
    public GraphShardSortDirection Direction { get; init; } = GraphShardSortDirection.Asc;
}

public sealed record class GraphShardRelationshipSortSpec
{
    public string? PropertyName { get; init; }
    public GraphShardSortDirection Direction { get; init; } = GraphShardSortDirection.Asc;
}

public sealed record class GraphShardPageSpec
{
    public int Offset { get; init; }
    public int Limit { get; init; } = int.MaxValue;
}

public sealed record class GraphShardCursorPageSpec
{
    public string? AfterCursor { get; init; }
    public int Limit { get; init; } = 50;
}

public sealed record class GraphShardCursorPageResult<T>
{
    public List<T> Items { get; init; } = [];
    public string? NextCursor { get; init; }
}

public sealed record class GraphShardTopNSpec
{
    public int Limit { get; init; } = 10;
    public string? AggregateKey { get; init; }
    public GraphShardSortDirection Direction { get; init; } = GraphShardSortDirection.Desc;
}

public sealed record class GraphShardNeighborSummaryRow
{
    public string RelType { get; init; } = string.Empty;
    public string TargetTable { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int Rank { get; init; }
    public int Count { get; init; }
    public int TotalCount { get; init; }
    public decimal Share { get; init; }
    public int CumulativeCount { get; init; }
    public decimal CumulativeShare { get; init; }
    public Dictionary<string, object?> AggregateValues { get; init; } = new(StringComparer.Ordinal);
}

public sealed record class GraphShardNeighborSummaryResult
{
    public string SourceNodeId { get; init; } = string.Empty;
    public int TotalCount { get; init; }
    public List<GraphShardNeighborSummaryRow> Rows { get; init; } = [];
}

public sealed record class GraphShardRelationshipSummaryRow
{
    public string RelType { get; init; } = string.Empty;
    public string SourceTable { get; init; } = string.Empty;
    public string TargetTable { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int Rank { get; init; }
    public int Count { get; init; }
    public int TotalCount { get; init; }
    public decimal Share { get; init; }
    public int CumulativeCount { get; init; }
    public decimal CumulativeShare { get; init; }
    public Dictionary<string, object?> AggregateValues { get; init; } = new(StringComparer.Ordinal);
}

public sealed record class GraphShardRelationshipSummaryResult
{
    public int TotalCount { get; init; }
    public List<GraphShardRelationshipSummaryRow> Rows { get; init; } = [];
}

public sealed record class GraphShardHistogramBucket
{
    public string Label { get; init; } = string.Empty;
    public decimal StartInclusive { get; init; }
    public decimal EndExclusive { get; init; }
    public int Count { get; init; }
    public int TotalCount { get; init; }
    public decimal Share { get; init; }
}

public sealed record class GraphShardHistogramResult
{
    public string PropertyName { get; init; } = string.Empty;
    public int TotalCount { get; init; }
    public List<GraphShardHistogramBucket> Buckets { get; init; } = [];
}

public sealed record class GraphShardNumericRangeSpec
{
    public string PropertyName { get; init; } = string.Empty;
    public decimal? StartInclusive { get; init; }
    public decimal? EndExclusive { get; init; }
}

public enum GraphShardTimeBucketInterval
{
    Hour,
    Day,
    Month,
    Year
}

public sealed record class GraphShardTimeBucket
{
    public string BucketKey { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string StartInclusive { get; init; } = string.Empty;
    public string EndExclusive { get; init; } = string.Empty;
    public int Count { get; init; }
    public int TotalCount { get; init; }
    public decimal Share { get; init; }
}

public sealed record class GraphShardTimeBucketResult
{
    public string PropertyName { get; init; } = string.Empty;
    public GraphShardTimeBucketInterval Interval { get; init; }
    public int TotalCount { get; init; }
    public List<GraphShardTimeBucket> Buckets { get; init; } = [];
}

public sealed record class GraphShardSummaryDeltaRow
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int Rank { get; init; }
    public int CurrentCount { get; init; }
    public int PreviousCount { get; init; }
    public int CountDelta { get; init; }
    public decimal CurrentShare { get; init; }
    public decimal PreviousShare { get; init; }
    public decimal ShareDelta { get; init; }
}

public sealed record class GraphShardSummaryDeltaResult
{
    public List<GraphShardSummaryDeltaRow> Rows { get; init; } = [];
}

public sealed record class GraphShardBucketDeltaRow
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int Rank { get; init; }
    public int CurrentCount { get; init; }
    public int PreviousCount { get; init; }
    public int CountDelta { get; init; }
    public decimal CurrentShare { get; init; }
    public decimal PreviousShare { get; init; }
    public decimal ShareDelta { get; init; }
}

public sealed record class GraphShardBucketDeltaResult
{
    public List<GraphShardBucketDeltaRow> Rows { get; init; } = [];
}

public enum GraphShardDeltaMetric
{
    CountDelta,
    ShareDelta
}

public sealed record class GraphShardDeltaTopNSpec
{
    public int Limit { get; init; } = 10;
    public GraphShardDeltaMetric Metric { get; init; } = GraphShardDeltaMetric.CountDelta;
    public GraphShardSortDirection Direction { get; init; } = GraphShardSortDirection.Desc;
    public bool UseAbsoluteValue { get; init; } = true;
}

public sealed record class GraphShardProjectionDeltaResult
{
    public List<string> AddedNodeIds { get; init; } = [];
    public List<string> RemovedNodeIds { get; init; } = [];
    public List<string> AddedRelIds { get; init; } = [];
    public List<string> RemovedRelIds { get; init; } = [];
    public int AddedNodeCount => AddedNodeIds.Count;
    public int RemovedNodeCount => RemovedNodeIds.Count;
    public int AddedRelCount => AddedRelIds.Count;
    public int RemovedRelCount => RemovedRelIds.Count;
}

public sealed record class GraphShardProjectionNodeDeltaRow
{
    public string TableName { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int Rank { get; init; }
    public int AddedCount { get; init; }
    public int RemovedCount { get; init; }
    public int NetDelta { get; init; }
}

public sealed record class GraphShardProjectionNodeDeltaResult
{
    public List<GraphShardProjectionNodeDeltaRow> Rows { get; init; } = [];
}

public sealed record class GraphShardProjectionRelationshipDeltaRow
{
    public string RelType { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int Rank { get; init; }
    public int AddedCount { get; init; }
    public int RemovedCount { get; init; }
    public int NetDelta { get; init; }
}

public sealed record class GraphShardProjectionRelationshipDeltaResult
{
    public List<GraphShardProjectionRelationshipDeltaRow> Rows { get; init; } = [];
}

public sealed record class GraphShardChangeReviewResult
{
    public GraphShardProjectionDeltaResult ProjectionDelta { get; init; } = new();
    public GraphShardProjectionNodeDeltaResult NodeChanges { get; init; } = new();
    public GraphShardProjectionRelationshipDeltaResult RelationshipChanges { get; init; } = new();
    public GraphShardProjectionNodeDeltaResult TopChangedNodeTables { get; init; } = new();
    public GraphShardProjectionRelationshipDeltaResult TopChangedRelationshipTypes { get; init; } = new();
}

public sealed record class GraphShardChangeReviewOverview
{
    public string Label { get; init; } = "extract_change_review";
    public int AddedNodeCount { get; init; }
    public int RemovedNodeCount { get; init; }
    public int AddedRelCount { get; init; }
    public int RemovedRelCount { get; init; }
    public int NetNodeDelta { get; init; }
    public int NetRelDelta { get; init; }
    public string SummaryLabel { get; init; } = string.Empty;
}

public sealed record class GraphShardChangeGroupSummary
{
    public int ChangedNodeTableCount { get; init; }
    public int GainingNodeTableCount { get; init; }
    public int DecliningNodeTableCount { get; init; }
    public int ChangedRelationshipTypeCount { get; init; }
    public int GainingRelationshipTypeCount { get; init; }
    public int DecliningRelationshipTypeCount { get; init; }
}

public sealed record class GraphShardChangeProjectionDrilldown
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int AddedCount { get; init; }
    public int RemovedCount { get; init; }
    public GraphShard AddedProjection { get; init; } = new();
    public GraphShard RemovedProjection { get; init; } = new();
}

public sealed record class GraphShardSelectedChangeSummary
{
    public int SelectedNodeTableCount { get; init; }
    public int SelectedRelationshipTypeCount { get; init; }
    public int AddedNodeCount { get; init; }
    public int RemovedNodeCount { get; init; }
    public int AddedRelCount { get; init; }
    public int RemovedRelCount { get; init; }
    public string SummaryLabel { get; init; } = string.Empty;
}

public sealed record class GraphShardSelectedChangeSummaryDelta
{
    public int SelectedNodeTableCountDelta { get; init; }
    public int SelectedRelationshipTypeCountDelta { get; init; }
    public int AddedNodeCountDelta { get; init; }
    public int RemovedNodeCountDelta { get; init; }
    public int AddedRelCountDelta { get; init; }
    public int RemovedRelCountDelta { get; init; }
    public string SummaryLabel { get; init; } = string.Empty;
}

public sealed record class GraphShardComposedChangeProjection
{
    public string Scope { get; init; } = string.Empty;
    public List<string> Keys { get; init; } = [];
    public List<GraphShardChangeProjectionDrilldown> Drilldowns { get; init; } = [];
    public GraphShard AddedProjection { get; init; } = new();
    public GraphShard RemovedProjection { get; init; } = new();
    public GraphShardSelectedChangeSummary Summary { get; init; } = new();
}

public sealed record class GraphShardComposedChangeProjectionComparison
{
    public string Scope { get; init; } = string.Empty;
    public List<string> CurrentKeys { get; init; } = [];
    public List<string> PreviousKeys { get; init; } = [];
    public GraphShardSelectedChangeSummaryDelta SummaryDelta { get; init; } = new();
    public GraphShardProjectionDeltaResult AddedProjectionDelta { get; init; } = new();
    public GraphShardProjectionDeltaResult RemovedProjectionDelta { get; init; } = new();
}

public sealed record class GraphShardChangeReviewHighlights
{
    public GraphShardChangeReviewOverview Overview { get; init; } = new();
    public GraphShardChangeGroupSummary GroupSummary { get; init; } = new();
    public GraphShardChangeReviewResult Review { get; init; } = new();
    public GraphShardProjectionNodeDeltaResult TopGainingNodeTables { get; init; } = new();
    public GraphShardProjectionNodeDeltaResult TopDecliningNodeTables { get; init; } = new();
    public GraphShardProjectionRelationshipDeltaResult TopGainingRelationshipTypes { get; init; } = new();
    public GraphShardProjectionRelationshipDeltaResult TopDecliningRelationshipTypes { get; init; } = new();
}

public sealed record class GraphShardChangeReviewDrilldown
{
    public GraphShardChangeReviewOverview Overview { get; init; } = new();
    public GraphShardChangeGroupSummary GroupSummary { get; init; } = new();
    public GraphShardChangeReviewResult Review { get; init; } = new();
    public List<GraphShardChangeProjectionDrilldown> NodeTableDrilldowns { get; init; } = [];
    public List<GraphShardChangeProjectionDrilldown> RelationshipTypeDrilldowns { get; init; } = [];
}

public sealed record class GraphShardMultiGroupChangeReview
{
    public GraphShardChangeReviewOverview Overview { get; init; } = new();
    public GraphShardChangeGroupSummary GroupSummary { get; init; } = new();
    public GraphShardChangeReviewResult Review { get; init; } = new();
    public GraphShardSelectedChangeSummary SelectionSummary { get; init; } = new();
    public GraphShardComposedChangeProjection NodeTableComposition { get; init; } = new();
    public GraphShardComposedChangeProjection RelationshipTypeComposition { get; init; } = new();
    public GraphShardComposedChangeProjection CombinedComposition { get; init; } = new();
}

public sealed record class GraphShardMultiGroupChangeReviewComparison
{
    public GraphShardSelectedChangeSummaryDelta SelectionSummaryDelta { get; init; } = new();
    public GraphShardComposedChangeProjectionComparison NodeTableCompositionComparison { get; init; } = new();
    public GraphShardComposedChangeProjectionComparison RelationshipTypeCompositionComparison { get; init; } = new();
    public GraphShardComposedChangeProjectionComparison CombinedCompositionComparison { get; init; } = new();
}

public sealed record class GraphShardMultiGroupChangeReviewSeriesRow
{
    public int Index { get; init; }
    public string Label { get; init; } = string.Empty;
    public GraphShardMultiGroupChangeReviewComparison Comparison { get; init; } = new();
}

public sealed record class GraphShardMultiGroupChangeReviewSeriesSummary
{
    public int BaselineIndex { get; init; }
    public int ComparisonCount { get; init; }
    public int MaxAddedNodeCountDelta { get; init; }
    public int MaxRemovedNodeCountDelta { get; init; }
    public int MaxAddedRelCountDelta { get; init; }
    public int MaxRemovedRelCountDelta { get; init; }
    public string SummaryLabel { get; init; } = string.Empty;
}

public sealed record class GraphShardMultiGroupChangeReviewSeriesComparison
{
    public int BaselineIndex { get; init; }
    public List<GraphShardMultiGroupChangeReviewSeriesRow> Rows { get; init; } = [];
    public GraphShardMultiGroupChangeReviewSeriesSummary Summary { get; init; } = new();
}

public sealed record class GraphShardNamedMultiGroupChangeReviewSeriesRow
{
    public string Key { get; init; } = string.Empty;
    public GraphShardMultiGroupChangeReviewComparison Comparison { get; init; } = new();
}

public sealed record class GraphShardNamedMultiGroupChangeReviewSeriesSummary
{
    public string BaselineKey { get; init; } = string.Empty;
    public int ComparisonCount { get; init; }
    public int MaxAddedNodeCountDelta { get; init; }
    public int MaxRemovedNodeCountDelta { get; init; }
    public int MaxAddedRelCountDelta { get; init; }
    public int MaxRemovedRelCountDelta { get; init; }
    public string SummaryLabel { get; init; } = string.Empty;
}

public sealed record class GraphShardNamedMultiGroupChangeReviewSeriesComparison
{
    public string BaselineKey { get; init; } = string.Empty;
    public List<GraphShardNamedMultiGroupChangeReviewSeriesRow> Rows { get; init; } = [];
    public GraphShardNamedMultiGroupChangeReviewSeriesSummary Summary { get; init; } = new();
}

public sealed record class GraphShardMultiGroupChangeReviewMatrixCell
{
    public string CurrentKey { get; init; } = string.Empty;
    public string PreviousKey { get; init; } = string.Empty;
    public GraphShardMultiGroupChangeReviewComparison Comparison { get; init; } = new();
}

public sealed record class GraphShardMultiGroupChangeReviewMatrixSummary
{
    public int ReviewCount { get; init; }
    public int ComparisonCount { get; init; }
    public int MaxAddedNodeCountDelta { get; init; }
    public int MaxRemovedNodeCountDelta { get; init; }
    public int MaxAddedRelCountDelta { get; init; }
    public int MaxRemovedRelCountDelta { get; init; }
    public string SummaryLabel { get; init; } = string.Empty;
}

public sealed record class GraphShardMultiGroupChangeReviewMatrixComparison
{
    public List<string> Keys { get; init; } = [];
    public List<GraphShardMultiGroupChangeReviewMatrixCell> Cells { get; init; } = [];
    public GraphShardMultiGroupChangeReviewMatrixSummary Summary { get; init; } = new();
}

public sealed record class GraphShardProjectionOverlapResult
{
    public List<string> CommonAddedNodeIds { get; init; } = [];
    public List<string> CommonRemovedNodeIds { get; init; } = [];
    public List<string> CommonAddedRelIds { get; init; } = [];
    public List<string> CommonRemovedRelIds { get; init; } = [];
    public int CommonAddedNodeCount => CommonAddedNodeIds.Count;
    public int CommonRemovedNodeCount => CommonRemovedNodeIds.Count;
    public int CommonAddedRelCount => CommonAddedRelIds.Count;
    public int CommonRemovedRelCount => CommonRemovedRelIds.Count;
}

public sealed record class GraphShardMultiGroupChangeReviewOverlap
{
    public List<string> Keys { get; init; } = [];
    public List<string> CommonNodeTableKeys { get; init; } = [];
    public List<string> CommonRelationshipTypeKeys { get; init; } = [];
    public GraphShardProjectionOverlapResult NodeTableCompositionOverlap { get; init; } = new();
    public GraphShardProjectionOverlapResult RelationshipTypeCompositionOverlap { get; init; } = new();
    public GraphShardProjectionOverlapResult CombinedCompositionOverlap { get; init; } = new();
}

public sealed record class GraphShardMultiGroupChangeReviewConsensus
{
    public List<string> Keys { get; init; } = [];
    public int ScopeCount { get; init; }
    public GraphShardSelectedChangeSummary CommonSummary { get; init; } = new();
    public GraphShardMultiGroupChangeReviewOverlap Overlap { get; init; } = new();
}

public sealed record class GraphShardScopeFrequencyRow
{
    public string Key { get; init; } = string.Empty;
    public int ScopeCount { get; init; }
    public decimal ScopeShare { get; init; }
}

public sealed record class GraphShardProjectionFrequencyResult
{
    public List<GraphShardScopeFrequencyRow> AddedNodes { get; init; } = [];
    public List<GraphShardScopeFrequencyRow> RemovedNodes { get; init; } = [];
    public List<GraphShardScopeFrequencyRow> AddedRelationships { get; init; } = [];
    public List<GraphShardScopeFrequencyRow> RemovedRelationships { get; init; } = [];
}

public sealed record class GraphShardMultiGroupChangeReviewFrequency
{
    public List<string> Keys { get; init; } = [];
    public int ScopeCount { get; init; }
    public List<GraphShardScopeFrequencyRow> NodeTableKeys { get; init; } = [];
    public List<GraphShardScopeFrequencyRow> RelationshipTypeKeys { get; init; } = [];
    public GraphShardProjectionFrequencyResult NodeTableCompositionFrequency { get; init; } = new();
    public GraphShardProjectionFrequencyResult RelationshipTypeCompositionFrequency { get; init; } = new();
    public GraphShardProjectionFrequencyResult CombinedCompositionFrequency { get; init; } = new();
}

public sealed record class GraphShardMultiGroupChangeReviewThresholdConsensus
{
    public List<string> Keys { get; init; } = [];
    public int ScopeCount { get; init; }
    public int MinScopeCount { get; init; }
    public GraphShardSelectedChangeSummary ThresholdSummary { get; init; } = new();
    public GraphShardMultiGroupChangeReviewFrequency Frequency { get; init; } = new();
    public List<string> QualifiedNodeTableKeys { get; init; } = [];
    public List<string> QualifiedRelationshipTypeKeys { get; init; } = [];
    public GraphShardProjectionOverlapResult ThresholdProjectionOverlap { get; init; } = new();
}

public sealed record class GraphShardMultiGroupChangeReviewSelectionProfile
{
    public string Key { get; init; } = string.Empty;
    public List<string> SelectedNodeTableKeys { get; init; } = [];
    public List<string> SelectedRelationshipTypeKeys { get; init; } = [];
    public int SelectedNodeTableCount { get; init; }
    public int SelectedRelationshipTypeCount { get; init; }
    public int AddedNodeCount { get; init; }
    public int RemovedNodeCount { get; init; }
    public int AddedRelCount { get; init; }
    public int RemovedRelCount { get; init; }
    public string Signature { get; init; } = string.Empty;
}

public sealed record class GraphShardMultiGroupChangeReviewSelectionProfiles
{
    public List<string> Keys { get; init; } = [];
    public List<GraphShardMultiGroupChangeReviewSelectionProfile> Profiles { get; init; } = [];
}

public sealed record class GraphShardMultiGroupChangeReviewSelectionFamily
{
    public string Signature { get; init; } = string.Empty;
    public List<string> Keys { get; init; } = [];
    public List<string> SelectedNodeTableKeys { get; init; } = [];
    public List<string> SelectedRelationshipTypeKeys { get; init; } = [];
    public int ScopeCount { get; init; }
}

public sealed record class GraphShardMultiGroupChangeReviewSelectionFamilies
{
    public List<string> Keys { get; init; } = [];
    public List<GraphShardMultiGroupChangeReviewSelectionFamily> Families { get; init; } = [];
}

public sealed record class GraphShardMultiGroupChangeReviewSelectionProfileDelta
{
    public string CurrentKey { get; init; } = string.Empty;
    public string PreviousKey { get; init; } = string.Empty;
    public int SelectedNodeTableCountDelta { get; init; }
    public int SelectedRelationshipTypeCountDelta { get; init; }
    public List<string> AddedNodeTableKeys { get; init; } = [];
    public List<string> RemovedNodeTableKeys { get; init; } = [];
    public List<string> AddedRelationshipTypeKeys { get; init; } = [];
    public List<string> RemovedRelationshipTypeKeys { get; init; } = [];
    public string SummaryLabel { get; init; } = string.Empty;
}

public sealed record class GraphShardSelectionFamilyDeltaRow
{
    public string Signature { get; init; } = string.Empty;
    public int CurrentScopeCount { get; init; }
    public int PreviousScopeCount { get; init; }
    public int ScopeCountDelta { get; init; }
    public List<string> SelectedNodeTableKeys { get; init; } = [];
    public List<string> SelectedRelationshipTypeKeys { get; init; } = [];
    public string SummaryLabel { get; init; } = string.Empty;
}

public sealed record class GraphShardSelectionFamilyDeltaResult
{
    public List<GraphShardSelectionFamilyDeltaRow> Rows { get; init; } = [];
}

public sealed record class GraphShardSelectionSignatureTransitionRow
{
    public string Key { get; init; } = string.Empty;
    public string PreviousSignature { get; init; } = string.Empty;
    public string CurrentSignature { get; init; } = string.Empty;
    public bool Changed { get; init; }
    public List<string> PreviousSelectedNodeTableKeys { get; init; } = [];
    public List<string> PreviousSelectedRelationshipTypeKeys { get; init; } = [];
    public List<string> CurrentSelectedNodeTableKeys { get; init; } = [];
    public List<string> CurrentSelectedRelationshipTypeKeys { get; init; } = [];
    public string SummaryLabel { get; init; } = string.Empty;
}

public sealed record class GraphShardSelectionSignatureTransitionResult
{
    public List<string> Keys { get; init; } = [];
    public List<GraphShardSelectionSignatureTransitionRow> Rows { get; init; } = [];
    public int ChangedScopeCount { get; init; }
    public int UnchangedScopeCount { get; init; }
}

public sealed record class GraphShardSelectionSignatureTransitionCountRow
{
    public string PreviousSignature { get; init; } = string.Empty;
    public string CurrentSignature { get; init; } = string.Empty;
    public int ScopeCount { get; init; }
    public bool Changed { get; init; }
    public string SummaryLabel { get; init; } = string.Empty;
}

public sealed record class GraphShardSelectionSignatureTransitionCountResult
{
    public List<GraphShardSelectionSignatureTransitionCountRow> Rows { get; init; } = [];
    public int ChangedTransitionCount { get; init; }
    public int UnchangedTransitionCount { get; init; }
}
