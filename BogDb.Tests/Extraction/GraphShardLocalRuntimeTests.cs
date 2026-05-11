using BogDb.Core.Extraction;
using Xunit;

namespace BogDb.Tests.Extraction;

public sealed class GraphShardLocalRuntimeTests
{
    [Fact]
    public void LocalRuntime_LoadAndMerge_ProvidesComposableLookupSurface()
    {
        var left = new GraphShard
        {
            GraphVersionToken = "graph-v1",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice", Properties = new Dictionary<string, object?> { ["name"] = "Alice" } }
                    ]
                }
            },
            Stats = new GraphShardStats { NodeCount = 1, EdgeCount = 0, NodeTableCount = 1, RelTableCount = 0 }
        };

        var right = new GraphShard
        {
            GraphVersionToken = "graph-v2",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice", Properties = new Dictionary<string, object?> { ["age"] = 30 } },
                        new NodeShardRow { ExternalId = "node:Person:bob", Properties = new Dictionary<string, object?> { ["name"] = "Bob" } }
                    ]
                }
            },
            RelTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal)
            {
                ["KNOWS"] = new()
                {
                    RelType = "KNOWS",
                    FromTable = "Person",
                    ToTable = "Person",
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob" }
                    ]
                }
            },
            Adjacency = new GraphShardAdjacency
            {
                Outgoing = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal)
                {
                    ["node:Person:alice"] = [new ShardEdgeRef { RelId = "rel:KNOWS:1", RelType = "KNOWS", NeighborNodeId = "node:Person:bob", Direction = "out" }]
                },
                Incoming = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal)
                {
                    ["node:Person:bob"] = [new ShardEdgeRef { RelId = "rel:KNOWS:1", RelType = "KNOWS", NeighborNodeId = "node:Person:alice", Direction = "in" }]
                }
            },
            Stats = new GraphShardStats { NodeCount = 2, EdgeCount = 1, NodeTableCount = 1, RelTableCount = 1 }
        };

        var runtime = GraphShardLocalRuntime.LoadExtract(left);
        runtime.MergeExtract(right);

        Assert.True(runtime.HasNode("node:Person:alice"));
        Assert.True(runtime.TryGetNodeRow("node:Person:alice", out var alice));
        Assert.Equal("Alice", alice.Properties["name"]);
        Assert.Equal(30, alice.Properties["age"]);
        Assert.Equal(["node:Person:bob"], runtime.Expand("node:Person:alice", includeOutgoing: true));
        Assert.Equal("merged:graph-v1|graph-v2", runtime.Shard.GraphVersionToken);
    }

    [Fact]
    public void LocalRuntime_ProjectNeighborhood_ReturnsCanonicalSubgraphWithBoundaryHints()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var projected = runtime.ProjectNeighborhood(
            "node:Person:alice",
            depth: 1,
            includeOutgoing: true,
            includeIncoming: false);

        Assert.Equal("runtime_projection", projected.ExtractionPolicy);
        Assert.False(projected.IsComplete);
        Assert.True(projected.Boundary.IsTruncated);
        Assert.True(projected.Boundary.TruncatedByDepth);
        Assert.Equal(["node:Person:bob"], projected.Boundary.BoundaryNodeIds);
        Assert.Equal(3, projected.Stats.NodeCount);
        Assert.Equal(2, projected.Stats.EdgeCount);
        Assert.Contains(projected.NodeTables["Person"].Rows, row => row.ExternalId == "node:Person:alice");
        Assert.Contains(projected.RelTables["KNOWS"].Rows, row => row.RelId == "rel:KNOWS:1");
        Assert.DoesNotContain(projected.NodeTables["Person"].Rows, row => row.ExternalId == "node:Person:carol");
    }

    [Fact]
    public void LocalRuntime_FilterNodes_AppliesTypedPredicateSpec()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var matches = runtime.FilterNodes(new GraphShardNodePredicateSpec
        {
            TableName = "Person",
            PropertyName = "name",
            Operator = GraphShardPredicateOperator.StartsWith,
            Value = "A"
        });

        var ageMatches = runtime.FilterNodes(new GraphShardNodePredicateSpec
        {
            TableName = "Person",
            PropertyName = "age",
            Operator = GraphShardPredicateOperator.Exists
        });

        Assert.Single(matches);
        Assert.Equal("node:Person:alice", matches[0].Row.ExternalId);
        Assert.Equal(
            ["node:Person:bob", "node:Person:carol"],
            ageMatches.Select(match => match.Row.ExternalId).OrderBy(x => x));
    }

    [Fact]
    public void LocalRuntime_FindPath_ReturnsBoundedShortestPath()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var path = runtime.FindPath(
            "node:Person:alice",
            "node:Person:carol",
            new GraphShardPathOptions
            {
                IncludeOutgoing = true,
                IncludeIncoming = false,
                RelType = "KNOWS",
                MaxDepth = 3
            });

        var blocked = runtime.FindPath(
            "node:Person:alice",
            "node:Person:carol",
            new GraphShardPathOptions
            {
                IncludeOutgoing = true,
                IncludeIncoming = false,
                RelType = "KNOWS",
                MaxDepth = 1
            });

        Assert.NotNull(path);
        Assert.Equal(
            ["node:Person:alice", "node:Person:bob", "node:Person:carol"],
            path!.NodeIds);
        Assert.Equal(["rel:KNOWS:1", "rel:KNOWS:2"], path.RelIds);
        Assert.Null(blocked);
    }

    [Fact]
    public void LocalRuntime_ProjectPath_MaterializesCanonicalPathSubgraph()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var projected = runtime.ProjectPath(
            "node:Person:alice",
            "node:Person:carol",
            new GraphShardPathOptions
            {
                IncludeOutgoing = true,
                IncludeIncoming = false,
                RelType = "KNOWS",
                MaxDepth = 3
            });

        Assert.NotNull(projected);
        Assert.Equal(
            ["node:Person:alice", "node:Person:bob", "node:Person:carol"],
            projected!.Path.NodeIds);
        Assert.Equal(3, projected.Shard.Stats.NodeCount);
        Assert.Equal(2, projected.Shard.Stats.EdgeCount);
        Assert.Single(projected.Shard.RelTables);
        Assert.True(projected.Shard.RelTables.ContainsKey("KNOWS"));
        Assert.DoesNotContain(projected.Shard.NodeTables.Keys, key => key == "City");
        Assert.True(projected.Shard.IsComplete);
    }

    [Fact]
    public void LocalRuntime_FilterNodes_SupportsMembershipAndExternalIdSelectors()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var tableFiltered = runtime.FilterNodes(new GraphShardNodePredicateSpec
        {
            TableNames = ["Person", "City"],
            ExternalIds = ["node:Person:bob", "node:City:seattle"],
            PropertyName = "name",
            Operator = GraphShardPredicateOperator.In,
            Values = ["Bob", "Seattle"]
        });

        var excluded = runtime.FilterNodes(new GraphShardNodePredicateSpec
        {
            TableNames = ["Person"],
            PropertyName = "name",
            Operator = GraphShardPredicateOperator.NotIn,
            Values = ["Alice", "Bob"]
        });

        Assert.Equal(
            ["node:City:seattle", "node:Person:bob"],
            tableFiltered.Select(match => match.Row.ExternalId).OrderBy(x => x));
        Assert.Equal(["node:Person:carol"], excluded.Select(match => match.Row.ExternalId));
    }

    [Fact]
    public void LocalRuntime_FilterNodes_SupportsBooleanComposition()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var filter = new GraphShardNodeFilterSpec
        {
            Composition = GraphShardFilterComposition.Any,
            Children =
            [
                new GraphShardNodeFilterSpec
                {
                    Predicate = new GraphShardNodePredicateSpec
                    {
                        TableNames = ["Person"],
                        PropertyName = "age",
                        Operator = GraphShardPredicateOperator.GreaterThanOrEqual,
                        Value = 28
                    }
                },
                new GraphShardNodeFilterSpec
                {
                    Predicate = new GraphShardNodePredicateSpec
                    {
                        TableNames = ["City"],
                        PropertyName = "name",
                        Operator = GraphShardPredicateOperator.Equal,
                        Value = "Seattle"
                    }
                }
            ],
            Not = new GraphShardNodeFilterSpec
            {
                Predicate = new GraphShardNodePredicateSpec
                {
                    PropertyName = "name",
                    Operator = GraphShardPredicateOperator.Equal,
                    Value = "Alice"
                }
            }
        };

        var matches = runtime.FilterNodes(filter);

        Assert.Equal(
            ["node:City:seattle", "node:Person:bob"],
            matches.Select(match => match.Row.ExternalId).OrderBy(x => x));
    }

    [Fact]
    public void LocalRuntime_ProjectPath_ExposesResolvedMetadata()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var projected = runtime.ProjectPath(
            "node:Person:alice",
            "node:Person:carol",
            new GraphShardPathOptions
            {
                IncludeOutgoing = true,
                IncludeIncoming = false,
                RelType = "KNOWS",
                MaxDepth = 3
            });

        Assert.NotNull(projected);
        Assert.Equal("node:Person:alice", projected!.StartNode.Row.ExternalId);
        Assert.Equal("node:Person:carol", projected.EndNode.Row.ExternalId);
        Assert.Equal(
            ["node:Person:alice", "node:Person:bob", "node:Person:carol"],
            projected.Nodes.Select(match => match.Row.ExternalId));
        Assert.Equal(
            ["rel:KNOWS:1", "rel:KNOWS:2"],
            projected.Relationships.Select(match => match.Row.RelId));
        Assert.All(projected.Relationships, match => Assert.Equal("KNOWS", match.RelType));
        Assert.Equal(["Person", "Person", "Person"], projected.NodeTableSequence);
        Assert.Equal(["KNOWS", "KNOWS"], projected.RelTypeSequence);
    }

    [Fact]
    public void LocalRuntime_ProjectFilteredSubgraph_MaterializesInducedSubgraph()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var projected = runtime.ProjectFilteredSubgraph(
            new GraphShardNodeFilterSpec
            {
                Predicate = new GraphShardNodePredicateSpec
                {
                    TableNames = ["Person"],
                    PropertyName = "age",
                    Operator = GraphShardPredicateOperator.GreaterThanOrEqual,
                    Value = 26
                }
            },
            includeOutgoing: true,
            includeIncoming: true,
            relType: "KNOWS");

        Assert.Equal("runtime_filter_projection", projected.ExtractionPolicy);
        Assert.Equal(2, projected.Stats.NodeCount);
        Assert.Equal(1, projected.Stats.EdgeCount);
        Assert.True(projected.RelTables.ContainsKey("KNOWS"));
        Assert.DoesNotContain(projected.NodeTables.Keys, key => key == "City");
        Assert.Equal(2, projected.SeedProvenance.IncludedCount);
    }

    [Fact]
    public void LocalRuntime_FilterRelationships_AndProjectFilteredRelationships_Work()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var matches = runtime.FilterRelationships(new GraphShardRelationshipFilterSpec
        {
            Predicate = new GraphShardRelationshipPredicateSpec
            {
                RelTypes = ["KNOWS"],
                PropertyName = "weight",
                Operator = GraphShardPredicateOperator.GreaterThanOrEqual,
                Value = 4
            }
        });

        var projected = runtime.ProjectFilteredRelationships(new GraphShardRelationshipFilterSpec
        {
            Predicate = new GraphShardRelationshipPredicateSpec
            {
                RelTypes = ["KNOWS"],
                PropertyName = "weight",
                Operator = GraphShardPredicateOperator.GreaterThanOrEqual,
                Value = 4
            }
        });

        Assert.Equal(["rel:KNOWS:1"], matches.Select(match => match.Row.RelId));
        Assert.Equal("runtime_relationship_projection", projected.ExtractionPolicy);
        Assert.Equal(2, projected.Stats.NodeCount);
        Assert.Equal(1, projected.Stats.EdgeCount);
        Assert.True(projected.RelTables.ContainsKey("KNOWS"));
    }

    [Fact]
    public void LocalRuntime_Aggregates_NodesAndRelationships()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var nodeAggregate = runtime.AggregateNodes(
            new GraphShardNodeFilterSpec
            {
                Predicate = new GraphShardNodePredicateSpec
                {
                    TableNames = ["Person"],
                    PropertyName = "age",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            groupByProperty: "age",
            new GraphShardAggregateSpec { Key = "count", Function = GraphShardAggregateFunction.Count });

        var relAggregate = runtime.AggregateRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    RelTypes = ["KNOWS"],
                    PropertyName = "weight",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            groupByProperty: "relType",
            new GraphShardAggregateSpec { Key = "count", Function = GraphShardAggregateFunction.Count },
            new GraphShardAggregateSpec { Key = "weightSum", Function = GraphShardAggregateFunction.Sum, PropertyName = "weight" });

        Assert.Equal(["26", "28"], nodeAggregate.Rows.Select(row => row.GroupKey));
        Assert.All(nodeAggregate.Rows, row => Assert.Equal(1, Convert.ToInt32(row.Values["count"])));
        Assert.Single(relAggregate.Rows);
        Assert.Equal("KNOWS", relAggregate.Rows[0].GroupKey);
        Assert.Equal(2, Convert.ToInt32(relAggregate.Rows[0].Values["count"]));
        Assert.Equal(7m, Convert.ToDecimal(relAggregate.Rows[0].Values["weightSum"]));
    }

    [Fact]
    public void LocalRuntime_SortAndPage_NodesAndRelationships_WorkDeterministically()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var nodeMatches = runtime.FilterNodes(new GraphShardNodeFilterSpec
        {
            Predicate = new GraphShardNodePredicateSpec
            {
                TableNames = ["Person"],
                PropertyName = "name",
                Operator = GraphShardPredicateOperator.Exists
            }
        });
        var relMatches = runtime.FilterRelationships(new GraphShardRelationshipFilterSpec
        {
            Predicate = new GraphShardRelationshipPredicateSpec
            {
                RelTypes = ["KNOWS"],
                PropertyName = "weight",
                Operator = GraphShardPredicateOperator.Exists
            }
        });

        var pagedNodes = runtime.SortAndPageNodes(
            nodeMatches,
            new GraphShardNodeSortSpec { PropertyName = "name", Direction = GraphShardSortDirection.Desc },
            new GraphShardPageSpec { Offset = 1, Limit = 1 });
        var pagedRelationships = runtime.SortAndPageRelationships(
            relMatches,
            new GraphShardRelationshipSortSpec { PropertyName = "weight", Direction = GraphShardSortDirection.Desc },
            new GraphShardPageSpec { Offset = 0, Limit = 1 });

        Assert.Equal(["node:Person:bob"], pagedNodes.Select(match => match.Row.ExternalId));
        Assert.Equal(["rel:KNOWS:1"], pagedRelationships.Select(match => match.Row.RelId));
    }

    [Fact]
    public void LocalRuntime_SummarizeNeighbors_GroupsByRelTypeAndTargetTable()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var summary = runtime.SummarizeNeighbors(
            "node:Person:alice",
            new GraphShardNodeFilterSpec
            {
                Composition = GraphShardFilterComposition.Any,
                Children =
                [
                    new GraphShardNodeFilterSpec
                    {
                        Predicate = new GraphShardNodePredicateSpec
                        {
                            TableNames = ["Person"],
                            PropertyName = "age",
                            Operator = GraphShardPredicateOperator.GreaterThanOrEqual,
                            Value = 28
                        }
                    },
                    new GraphShardNodeFilterSpec
                    {
                        Predicate = new GraphShardNodePredicateSpec
                        {
                            TableNames = ["City"],
                            PropertyName = "name",
                            Operator = GraphShardPredicateOperator.Equal,
                            Value = "Seattle"
                        }
                    }
                ]
            },
            includeOutgoing: true,
            includeIncoming: false);

        Assert.Equal("node:Person:alice", summary.SourceNodeId);
        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(2, summary.Rows.Count);
        Assert.Contains(summary.Rows, row => row.RelType == "KNOWS" && row.TargetTable == "Person" && row.Count == 1);
        Assert.Contains(summary.Rows, row => row.RelType == "LIVES_IN" && row.TargetTable == "City" && row.Count == 1);
        Assert.All(summary.Rows, row => Assert.Equal(0.5m, row.Share));
    }

    [Fact]
    public void LocalRuntime_CursorPageNodes_AndRelationships_SupportMultiKeySorts()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var nodeMatches = runtime.FilterNodes(new GraphShardNodeFilterSpec
        {
            Predicate = new GraphShardNodePredicateSpec
            {
                TableNames = ["Person"],
                PropertyName = "age",
                Operator = GraphShardPredicateOperator.Exists
            }
        });

        var firstNodePage = runtime.CursorPageNodes(
            nodeMatches,
            [
                new GraphShardNodeSortSpec { PropertyName = "age", Direction = GraphShardSortDirection.Desc },
                new GraphShardNodeSortSpec { PropertyName = "name", Direction = GraphShardSortDirection.Asc }
            ],
            new GraphShardCursorPageSpec { Limit = 1 });
        var secondNodePage = runtime.CursorPageNodes(
            nodeMatches,
            [
                new GraphShardNodeSortSpec { PropertyName = "age", Direction = GraphShardSortDirection.Desc },
                new GraphShardNodeSortSpec { PropertyName = "name", Direction = GraphShardSortDirection.Asc }
            ],
            new GraphShardCursorPageSpec { Limit = 1, AfterCursor = firstNodePage.NextCursor });

        var relMatches = runtime.FilterRelationships(new GraphShardRelationshipFilterSpec
        {
            Composition = GraphShardFilterComposition.Any,
            Children =
            [
                new GraphShardRelationshipFilterSpec
                {
                    Predicate = new GraphShardRelationshipPredicateSpec
                    {
                        RelTypes = ["KNOWS"],
                        PropertyName = "weight",
                        Operator = GraphShardPredicateOperator.Exists
                    }
                },
                new GraphShardRelationshipFilterSpec
                {
                    Predicate = new GraphShardRelationshipPredicateSpec
                    {
                        RelTypes = ["LIVES_IN"],
                        PropertyName = "since",
                        Operator = GraphShardPredicateOperator.Exists
                    }
                }
            ]
        });

        var firstRelPage = runtime.CursorPageRelationships(
            relMatches,
            [
                new GraphShardRelationshipSortSpec { PropertyName = "relType", Direction = GraphShardSortDirection.Asc },
                new GraphShardRelationshipSortSpec { PropertyName = "weight", Direction = GraphShardSortDirection.Desc }
            ],
            new GraphShardCursorPageSpec { Limit = 2 });
        var secondRelPage = runtime.CursorPageRelationships(
            relMatches,
            [
                new GraphShardRelationshipSortSpec { PropertyName = "relType", Direction = GraphShardSortDirection.Asc },
                new GraphShardRelationshipSortSpec { PropertyName = "weight", Direction = GraphShardSortDirection.Desc }
            ],
            new GraphShardCursorPageSpec { Limit = 2, AfterCursor = firstRelPage.NextCursor });

        Assert.Equal(["node:Person:bob"], firstNodePage.Items.Select(match => match.Row.ExternalId));
        Assert.Equal(["node:Person:carol"], secondNodePage.Items.Select(match => match.Row.ExternalId));
        Assert.NotNull(firstNodePage.NextCursor);
        Assert.Null(secondNodePage.NextCursor);

        Assert.Equal(["rel:KNOWS:1", "rel:KNOWS:2"], firstRelPage.Items.Select(match => match.Row.RelId));
        Assert.Equal(["rel:LIVES_IN:1"], secondRelPage.Items.Select(match => match.Row.RelId));
    }

    [Fact]
    public void LocalRuntime_SummarizeNeighbors_ComputesRelationshipAndNeighborSums()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var summary = runtime.SummarizeNeighbors(
            "node:Person:alice",
            new GraphShardNodeFilterSpec
            {
                Composition = GraphShardFilterComposition.Any,
                Children =
                [
                    new GraphShardNodeFilterSpec
                    {
                        Predicate = new GraphShardNodePredicateSpec
                        {
                            TableNames = ["Person"],
                            PropertyName = "age",
                            Operator = GraphShardPredicateOperator.GreaterThanOrEqual,
                            Value = 28
                        }
                    },
                    new GraphShardNodeFilterSpec
                    {
                        Predicate = new GraphShardNodePredicateSpec
                        {
                            TableNames = ["City"],
                            PropertyName = "name",
                            Operator = GraphShardPredicateOperator.Equal,
                            Value = "Seattle"
                        }
                    }
                ]
            },
            includeOutgoing: true,
            includeIncoming: false,
            relType: null,
            new GraphShardAggregateSpec { Key = "neighborAgeSum", Function = GraphShardAggregateFunction.Sum, PropertyName = "age", Source = GraphShardAggregateSource.Node },
            new GraphShardAggregateSpec { Key = "relationshipSinceSum", Function = GraphShardAggregateFunction.Sum, PropertyName = "since", Source = GraphShardAggregateSource.Relationship });

        var knows = Assert.Single(summary.Rows.Where(row => row.RelType == "KNOWS"));
        var livesIn = Assert.Single(summary.Rows.Where(row => row.RelType == "LIVES_IN"));

        Assert.Equal(28m, Convert.ToDecimal(knows.AggregateValues["neighborAgeSum"]));
        Assert.Equal(0m, Convert.ToDecimal(livesIn.AggregateValues["neighborAgeSum"]));
        Assert.Equal(2019m, Convert.ToDecimal(livesIn.AggregateValues["relationshipSinceSum"]));
    }

    [Fact]
    public void LocalRuntime_SummarizeRelationships_GroupsCountsAndSums()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var summary = runtime.SummarizeRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Composition = GraphShardFilterComposition.Any,
                Children =
                [
                    new GraphShardRelationshipFilterSpec
                    {
                        Predicate = new GraphShardRelationshipPredicateSpec
                        {
                            RelTypes = ["KNOWS"],
                            PropertyName = "weight",
                            Operator = GraphShardPredicateOperator.Exists
                        }
                    },
                    new GraphShardRelationshipFilterSpec
                    {
                        Predicate = new GraphShardRelationshipPredicateSpec
                        {
                            RelTypes = ["LIVES_IN"],
                            PropertyName = "since",
                            Operator = GraphShardPredicateOperator.Exists
                        }
                    }
                ]
            },
            new GraphShardAggregateSpec { Key = "count", Function = GraphShardAggregateFunction.Count, Source = GraphShardAggregateSource.Relationship },
            new GraphShardAggregateSpec { Key = "metricSum", Function = GraphShardAggregateFunction.Sum, PropertyName = "weight", Source = GraphShardAggregateSource.Relationship });

        var knows = Assert.Single(summary.Rows, row => row.RelType == "KNOWS");
        var livesIn = Assert.Single(summary.Rows, row => row.RelType == "LIVES_IN");

        Assert.Equal("Person", knows.SourceTable);
        Assert.Equal("Person", knows.TargetTable);
        Assert.Equal(2, knows.Count);
        Assert.Equal(3, knows.TotalCount);
        Assert.Equal(2, Convert.ToInt32(knows.AggregateValues["count"]));
        Assert.Equal(7m, Convert.ToDecimal(knows.AggregateValues["metricSum"]));

        Assert.Equal("Person", livesIn.SourceTable);
        Assert.Equal("City", livesIn.TargetTable);
        Assert.Equal(1, livesIn.Count);
        Assert.Equal(3, livesIn.TotalCount);
        Assert.Equal(1, Convert.ToInt32(livesIn.AggregateValues["count"]));
        Assert.Equal(0m, Convert.ToDecimal(livesIn.AggregateValues["metricSum"]));
        Assert.Equal(1m / 3m, livesIn.Share);
    }

    [Fact]
    public void LocalRuntime_Histograms_GroupNumericValuesIntoBuckets()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var nodeHistogram = runtime.HistogramNodes(
            new GraphShardNodeFilterSpec
            {
                Predicate = new GraphShardNodePredicateSpec
                {
                    TableNames = ["Person"],
                    PropertyName = "age",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            "age",
            5m);

        var relHistogram = runtime.HistogramRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    RelTypes = ["KNOWS"],
                    PropertyName = "weight",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            "weight",
            2m);

        Assert.Equal("age", nodeHistogram.PropertyName);
        Assert.Equal(2, nodeHistogram.TotalCount);
        Assert.Single(nodeHistogram.Buckets);
        Assert.Equal(25m, nodeHistogram.Buckets[0].StartInclusive);
        Assert.Equal(30m, nodeHistogram.Buckets[0].EndExclusive);
        Assert.Equal(2, nodeHistogram.Buckets[0].Count);
        Assert.Equal(1m, nodeHistogram.Buckets[0].Share);

        Assert.Equal("weight", relHistogram.PropertyName);
        Assert.Equal(2, relHistogram.TotalCount);
        Assert.Equal(2, relHistogram.Buckets.Count);
        Assert.Equal(2m, relHistogram.Buckets[0].StartInclusive);
        Assert.Equal(4m, relHistogram.Buckets[0].EndExclusive);
        Assert.Equal(1, relHistogram.Buckets[0].Count);
        Assert.Equal(0.5m, relHistogram.Buckets[0].Share);
        Assert.Equal(4m, relHistogram.Buckets[1].StartInclusive);
        Assert.Equal(6m, relHistogram.Buckets[1].EndExclusive);
        Assert.Equal(1, relHistogram.Buckets[1].Count);
        Assert.Equal(0.5m, relHistogram.Buckets[1].Share);
    }

    [Fact]
    public void LocalRuntime_ScopedFacetHelpers_RestrictByTableAndRelType()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var nodeHistogram = runtime.HistogramNodesForTable("Person", "age", 5m);
        var relationshipHistogram = runtime.HistogramRelationshipsForRelType("KNOWS", "weight", 2m);
        var neighborSummary = runtime.SummarizeNeighborsForTargetTable(
            "node:Person:alice",
            "Person",
            includeOutgoing: true,
            includeIncoming: false);
        var relationshipSummary = runtime.SummarizeRelationshipsForRelType(
            "KNOWS",
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    PropertyName = "weight",
                    Operator = GraphShardPredicateOperator.Exists
                }
            });

        Assert.Single(nodeHistogram.Buckets);
        Assert.Equal(2, nodeHistogram.TotalCount);
        Assert.Equal(2, relationshipHistogram.TotalCount);

        var neighborRow = Assert.Single(neighborSummary.Rows);
        Assert.Equal("KNOWS", neighborRow.RelType);
        Assert.Equal("Person", neighborRow.TargetTable);
        Assert.Equal(1m, neighborRow.Share);

        var relationshipRow = Assert.Single(relationshipSummary.Rows);
        Assert.Equal("KNOWS", relationshipRow.RelType);
        Assert.Equal(1m, relationshipRow.Share);
    }

    [Fact]
    public void LocalRuntime_RankSummaryHelpers_ComputeRanksAndCumulativeShares()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var neighborSummary = runtime.SummarizeNeighbors(
            "node:Person:alice",
            new GraphShardNodeFilterSpec
            {
                Composition = GraphShardFilterComposition.Any,
                Children =
                [
                    new GraphShardNodeFilterSpec
                    {
                        Predicate = new GraphShardNodePredicateSpec
                        {
                            TableNames = ["Person"],
                            PropertyName = "age",
                            Operator = GraphShardPredicateOperator.GreaterThanOrEqual,
                            Value = 28
                        }
                    },
                    new GraphShardNodeFilterSpec
                    {
                        Predicate = new GraphShardNodePredicateSpec
                        {
                            TableNames = ["City"],
                            PropertyName = "name",
                            Operator = GraphShardPredicateOperator.Equal,
                            Value = "Seattle"
                        }
                    }
                ]
            },
            includeOutgoing: true,
            includeIncoming: false);

        var ranked = runtime.RankNeighborSummaries(neighborSummary, new GraphShardTopNSpec { Limit = 2 });

        Assert.Equal(2, ranked.Rows.Count);
        Assert.Equal(1, ranked.Rows[0].Rank);
        Assert.Equal(1, ranked.Rows[0].CumulativeCount);
        Assert.Equal(0.5m, ranked.Rows[0].CumulativeShare);
        Assert.Equal(2, ranked.Rows[1].Rank);
        Assert.Equal(2, ranked.Rows[1].CumulativeCount);
        Assert.Equal(1m, ranked.Rows[1].CumulativeShare);
    }

    [Fact]
    public void LocalRuntime_TimeBucketHelpers_GroupDateLikeValues()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var nodeBuckets = runtime.TimeBucketNodes(
            new GraphShardNodeFilterSpec
            {
                Predicate = new GraphShardNodePredicateSpec
                {
                    TableNames = ["Person"],
                    PropertyName = "joinedAt",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            "joinedAt",
            GraphShardTimeBucketInterval.Month);
        var relationshipBuckets = runtime.TimeBucketRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    RelTypes = ["KNOWS"],
                    PropertyName = "recordedAt",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            "recordedAt",
            GraphShardTimeBucketInterval.Day);

        Assert.Equal(2, nodeBuckets.TotalCount);
        Assert.Equal(["2024-01", "2024-02"], nodeBuckets.Buckets.Select(bucket => bucket.BucketKey));
        Assert.Equal(0.5m, nodeBuckets.Buckets[0].Share);

        Assert.Equal(2, relationshipBuckets.TotalCount);
        Assert.Equal(["2024-01-15", "2024-01-16"], relationshipBuckets.Buckets.Select(bucket => bucket.BucketKey));
        Assert.Equal(0.5m, relationshipBuckets.Buckets[1].Share);
        Assert.Equal("2024-01", nodeBuckets.Buckets[0].Label);
        Assert.Equal("2024-01-15", relationshipBuckets.Buckets[0].Label);
    }

    [Fact]
    public void LocalRuntime_LabelAndCompareHelpers_ProduceStableReferenceOutput()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var currentNeighborSummary = runtime.RankNeighborSummaries(
            runtime.SummarizeNeighbors(
                "node:Person:alice",
                new GraphShardNodeFilterSpec
                {
                    Composition = GraphShardFilterComposition.Any,
                    Children =
                    [
                        new GraphShardNodeFilterSpec
                        {
                            Predicate = new GraphShardNodePredicateSpec
                            {
                                TableNames = ["Person"],
                                PropertyName = "age",
                                Operator = GraphShardPredicateOperator.GreaterThanOrEqual,
                                Value = 28
                            }
                        },
                        new GraphShardNodeFilterSpec
                        {
                            Predicate = new GraphShardNodePredicateSpec
                            {
                                TableNames = ["City"],
                                PropertyName = "name",
                                Operator = GraphShardPredicateOperator.Equal,
                                Value = "Seattle"
                            }
                        }
                    ]
                },
                includeOutgoing: true,
                includeIncoming: false),
            new GraphShardTopNSpec { Limit = 2 });

        var previousNeighborSummary = new GraphShardNeighborSummaryResult
        {
            SourceNodeId = "node:Person:alice",
            TotalCount = 1,
            Rows =
            [
                new GraphShardNeighborSummaryRow
                {
                    RelType = "KNOWS",
                    TargetTable = "Person",
                    Label = "KNOWS -> Person",
                    Count = 1,
                    TotalCount = 1,
                    Share = 1m,
                    Rank = 1,
                    CumulativeCount = 1,
                    CumulativeShare = 1m
                }
            ]
        };

        var currentTimeBuckets = runtime.TimeBucketNodes(
            new GraphShardNodeFilterSpec
            {
                Predicate = new GraphShardNodePredicateSpec
                {
                    TableNames = ["Person"],
                    PropertyName = "joinedAt",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            "joinedAt",
            GraphShardTimeBucketInterval.Month);
        var previousTimeBuckets = new GraphShardTimeBucketResult
        {
            PropertyName = "joinedAt",
            Interval = GraphShardTimeBucketInterval.Month,
            TotalCount = 1,
            Buckets =
            [
                new GraphShardTimeBucket
                {
                    BucketKey = "2024-01",
                    Label = "2024-01",
                    StartInclusive = "2024-01-01T00:00:00.0000000+00:00",
                    EndExclusive = "2024-02-01T00:00:00.0000000+00:00",
                    Count = 1,
                    TotalCount = 1,
                    Share = 1m
                }
            ]
        };

        var summaryDelta = runtime.CompareNeighborSummaries(currentNeighborSummary, previousNeighborSummary);
        var timeDelta = runtime.CompareTimeBuckets(currentTimeBuckets, previousTimeBuckets);

        Assert.Equal("KNOWS -> Person", currentNeighborSummary.Rows[0].Label);
        Assert.Equal("LIVES_IN -> City", currentNeighborSummary.Rows[1].Label);

        var personDelta = Assert.Single(summaryDelta.Rows, row => row.Key == "KNOWS|Person");
        Assert.Equal("KNOWS -> Person", personDelta.Label);
        Assert.Equal(0, personDelta.CountDelta);
        Assert.Equal(-0.5m, personDelta.ShareDelta);

        var monthDelta = Assert.Single(timeDelta.Rows, row => row.Key == "2024-01");
        Assert.Equal("2024-01", monthDelta.Label);
        Assert.Equal(0, monthDelta.CountDelta);
        Assert.Equal(-0.5m, monthDelta.ShareDelta);
    }

    [Fact]
    public void LocalRuntime_DeltaRankingAndProjectionDiff_ExposeBiggestMoversAndMembershipChanges()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());
        var previousNeighborSummary = new GraphShardNeighborSummaryResult
        {
            SourceNodeId = "node:Person:alice",
            TotalCount = 4,
            Rows =
            [
                new GraphShardNeighborSummaryRow
                {
                    RelType = "KNOWS",
                    TargetTable = "Person",
                    Label = "KNOWS -> Person",
                    Count = 3,
                    TotalCount = 4,
                    Share = 0.75m
                },
                new GraphShardNeighborSummaryRow
                {
                    RelType = "LIVES_IN",
                    TargetTable = "City",
                    Label = "LIVES_IN -> City",
                    Count = 1,
                    TotalCount = 4,
                    Share = 0.25m
                }
            ]
        };
        var currentNeighborSummary = runtime.SummarizeNeighbors(
            "node:Person:alice",
            new GraphShardNodeFilterSpec
            {
                Composition = GraphShardFilterComposition.Any,
                Children =
                [
                    new GraphShardNodeFilterSpec
                    {
                        Predicate = new GraphShardNodePredicateSpec
                        {
                            TableNames = ["Person"],
                            PropertyName = "age",
                            Operator = GraphShardPredicateOperator.GreaterThanOrEqual,
                            Value = 28
                        }
                    },
                    new GraphShardNodeFilterSpec
                    {
                        Predicate = new GraphShardNodePredicateSpec
                        {
                            TableNames = ["City"],
                            PropertyName = "name",
                            Operator = GraphShardPredicateOperator.Equal,
                            Value = "Seattle"
                        }
                    }
                ]
            },
            includeOutgoing: true,
            includeIncoming: false);
        var currentHistogram = runtime.HistogramRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    RelTypes = ["KNOWS"],
                    PropertyName = "weight",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            "weight",
            1m);
        var previousHistogram = new GraphShardHistogramResult
        {
            PropertyName = "weight",
            TotalCount = 3,
            Buckets =
            [
                new GraphShardHistogramBucket
                {
                    Label = "[3, 4)",
                    StartInclusive = 3m,
                    EndExclusive = 4m,
                    Count = 3,
                    TotalCount = 3,
                    Share = 1m
                }
            ]
        };
        var previousShard = new GraphShard
        {
            GraphVersionToken = "graph-v0",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice" },
                        new NodeShardRow { ExternalId = "node:Person:bob" }
                    ]
                }
            },
            RelTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal)
            {
                ["KNOWS"] = new()
                {
                    RelType = "KNOWS",
                    FromTable = "Person",
                    ToTable = "Person",
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob" }
                    ]
                }
            }
        };

        var summaryDelta = runtime.CompareNeighborSummaries(currentNeighborSummary, previousNeighborSummary);
        var bucketDelta = runtime.CompareHistogramBuckets(currentHistogram, previousHistogram);
        var rankedSummaryDelta = runtime.RankSummaryDeltas(
            summaryDelta,
            new GraphShardDeltaTopNSpec
            {
                Limit = 1,
                Metric = GraphShardDeltaMetric.CountDelta,
                Direction = GraphShardSortDirection.Desc,
                UseAbsoluteValue = true
            });
        var rankedBucketDelta = runtime.RankBucketDeltas(
            bucketDelta,
            new GraphShardDeltaTopNSpec
            {
                Limit = 1,
                Metric = GraphShardDeltaMetric.ShareDelta,
                Direction = GraphShardSortDirection.Desc,
                UseAbsoluteValue = true
            });
        var projectionDelta = runtime.CompareProjectedShards(previousShard);

        var topSummary = Assert.Single(rankedSummaryDelta.Rows);
        Assert.Equal("KNOWS|Person", topSummary.Key);
        Assert.Equal(1, topSummary.Rank);
        Assert.Equal(-2, topSummary.CountDelta);

        var topBucket = Assert.Single(rankedBucketDelta.Rows);
        Assert.Equal("3|4", topBucket.Key);
        Assert.Equal(1, topBucket.Rank);
        Assert.Equal(-0.5m, topBucket.ShareDelta);

        Assert.Equal(
            ["node:City:seattle", "node:Person:carol"],
            projectionDelta.AddedNodeIds);
        Assert.Empty(projectionDelta.RemovedNodeIds);
        Assert.Equal(
            ["rel:KNOWS:2", "rel:LIVES_IN:1"],
            projectionDelta.AddedRelIds);
        Assert.Empty(projectionDelta.RemovedRelIds);
    }

    [Fact]
    public void LocalRuntime_GainersDeclinersAndProjectionSummaries_GroupChangesForClientViews()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());
        var summaryDeltas = new GraphShardSummaryDeltaResult
        {
            Rows =
            [
                new GraphShardSummaryDeltaRow { Key = "KNOWS|Person", Label = "KNOWS -> Person", CountDelta = 3, ShareDelta = 0.40m },
                new GraphShardSummaryDeltaRow { Key = "LIVES_IN|City", Label = "LIVES_IN -> City", CountDelta = -2, ShareDelta = -0.25m },
                new GraphShardSummaryDeltaRow { Key = "WORKS_AT|Company", Label = "WORKS_AT -> Company", CountDelta = 1, ShareDelta = 0.05m }
            ]
        };
        var bucketDeltas = new GraphShardBucketDeltaResult
        {
            Rows =
            [
                new GraphShardBucketDeltaRow { Key = "1|2", Label = "[1, 2)", CountDelta = 2, ShareDelta = 0.25m },
                new GraphShardBucketDeltaRow { Key = "3|4", Label = "[3, 4)", CountDelta = -3, ShareDelta = -0.60m }
            ]
        };
        var previousShard = new GraphShard
        {
            GraphVersionToken = "graph-v0",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice" },
                        new NodeShardRow { ExternalId = "node:Person:bob" }
                    ]
                },
                ["City"] = new()
                {
                    TableName = "City",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:City:seattle" }
                    ]
                }
            },
            RelTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal)
            {
                ["KNOWS"] = new()
                {
                    RelType = "KNOWS",
                    FromTable = "Person",
                    ToTable = "Person",
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob" }
                    ]
                }
            }
        };

        var topGainers = runtime.TopGainingSummaryDeltas(summaryDeltas, new GraphShardDeltaTopNSpec { Limit = 1 });
        var topDecliners = runtime.TopDecliningSummaryDeltas(summaryDeltas, new GraphShardDeltaTopNSpec { Limit = 1, Metric = GraphShardDeltaMetric.ShareDelta });
        var bucketDecliners = runtime.TopDecliningBucketDeltas(bucketDeltas, new GraphShardDeltaTopNSpec { Limit = 1 });
        var nodeChangeSummary = runtime.SummarizeProjectedNodeChanges(previousShard);
        var relationshipChangeSummary = runtime.SummarizeProjectedRelationshipChanges(previousShard);

        var gainingRow = Assert.Single(topGainers.Rows);
        Assert.Equal("KNOWS|Person", gainingRow.Key);
        Assert.Equal(3, gainingRow.CountDelta);
        Assert.Equal(1, gainingRow.Rank);

        var decliningRow = Assert.Single(topDecliners.Rows);
        Assert.Equal("LIVES_IN|City", decliningRow.Key);
        Assert.Equal(-0.25m, decliningRow.ShareDelta);
        Assert.Equal(1, decliningRow.Rank);

        var bucketDecliningRow = Assert.Single(bucketDecliners.Rows);
        Assert.Equal("3|4", bucketDecliningRow.Key);
        Assert.Equal(-3, bucketDecliningRow.CountDelta);

        var personNodeDelta = Assert.Single(nodeChangeSummary.Rows, row => row.TableName == "Person");
        Assert.Equal(1, personNodeDelta.AddedCount);
        Assert.Equal(0, personNodeDelta.RemovedCount);
        Assert.Equal(1, personNodeDelta.NetDelta);
        Assert.DoesNotContain(nodeChangeSummary.Rows, row => row.TableName == "City");

        var knowsRelDelta = Assert.Single(relationshipChangeSummary.Rows, row => row.RelType == "KNOWS");
        Assert.Equal(1, knowsRelDelta.AddedCount);
        Assert.Equal(0, knowsRelDelta.RemovedCount);

        var livesInRelDelta = Assert.Single(relationshipChangeSummary.Rows, row => row.RelType == "LIVES_IN");
        Assert.Equal(1, livesInRelDelta.AddedCount);
        Assert.Equal(0, livesInRelDelta.RemovedCount);
    }

    [Fact]
    public void LocalRuntime_TopChangedAndChangeReview_ComposeProjectionDiffIntoReferenceFlow()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());
        var previousShard = new GraphShard
        {
            GraphVersionToken = "graph-v0",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice" },
                        new NodeShardRow { ExternalId = "node:Person:bob" }
                    ]
                },
                ["City"] = new()
                {
                    TableName = "City",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:City:seattle" }
                    ]
                }
            },
            RelTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal)
            {
                ["KNOWS"] = new()
                {
                    RelType = "KNOWS",
                    FromTable = "Person",
                    ToTable = "Person",
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob" }
                    ]
                }
            }
        };

        var topNodeChanges = runtime.TopChangedNodeTables(previousShard, new GraphShardTopNSpec { Limit = 1 });
        var topRelationshipChanges = runtime.TopChangedRelationshipTypes(previousShard, new GraphShardTopNSpec { Limit = 2 });
        var review = runtime.BuildChangeReview(previousShard, new GraphShardTopNSpec { Limit = 1 });

        var topNode = Assert.Single(topNodeChanges.Rows);
        Assert.Equal("Person", topNode.TableName);
        Assert.Equal(1, topNode.Rank);
        Assert.Equal(1, topNode.NetDelta);

        Assert.Equal(2, topRelationshipChanges.Rows.Count);
        Assert.Equal("KNOWS", topRelationshipChanges.Rows[0].RelType);
        Assert.Equal(1, topRelationshipChanges.Rows[0].Rank);

        Assert.Equal(["node:Person:carol"], review.ProjectionDelta.AddedNodeIds);
        Assert.Equal(["rel:KNOWS:2", "rel:LIVES_IN:1"], review.ProjectionDelta.AddedRelIds);
        Assert.Single(review.TopChangedNodeTables.Rows);
        Assert.Single(review.TopChangedRelationshipTypes.Rows);
        Assert.Equal("Person", review.TopChangedNodeTables.Rows[0].TableName);
        Assert.Equal("KNOWS", review.TopChangedRelationshipTypes.Rows[0].RelType);
    }

    [Fact]
    public void LocalRuntime_SignedGroupedChangeViewsAndHighlights_ExposeStableReviewReferenceData()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());
        var previousShard = new GraphShard
        {
            GraphVersionToken = "graph-v0",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice" },
                        new NodeShardRow { ExternalId = "node:Person:bob" }
                    ]
                },
                ["City"] = new()
                {
                    TableName = "City",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:City:seattle" },
                        new NodeShardRow { ExternalId = "node:City:portland" }
                    ]
                }
            },
            RelTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal)
            {
                ["KNOWS"] = new()
                {
                    RelType = "KNOWS",
                    FromTable = "Person",
                    ToTable = "Person",
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob" }
                    ]
                },
                ["WORKS_AT"] = new()
                {
                    RelType = "WORKS_AT",
                    FromTable = "Person",
                    ToTable = "Company",
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:WORKS_AT:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Company:acme" }
                    ]
                }
            }
        };

        var topGainingNodes = runtime.TopGainingNodeTables(previousShard, new GraphShardTopNSpec { Limit = 2 });
        var topDecliningNodes = runtime.TopDecliningNodeTables(previousShard, new GraphShardTopNSpec { Limit = 2 });
        var topGainingRelationships = runtime.TopGainingRelationshipTypes(previousShard, new GraphShardTopNSpec { Limit = 2 });
        var topDecliningRelationships = runtime.TopDecliningRelationshipTypes(previousShard, new GraphShardTopNSpec { Limit = 2 });
        var overview = runtime.BuildChangeReviewOverview(previousShard);
        var highlights = runtime.BuildChangeReviewHighlights(previousShard, new GraphShardTopNSpec { Limit = 1 });

        var gainingNode = Assert.Single(topGainingNodes.Rows);
        Assert.Equal("Person", gainingNode.TableName);
        Assert.Equal(1, gainingNode.NetDelta);
        Assert.Equal(1, gainingNode.Rank);

        var decliningNode = Assert.Single(topDecliningNodes.Rows);
        Assert.Equal("City", decliningNode.TableName);
        Assert.Equal(-1, decliningNode.NetDelta);
        Assert.Equal(1, decliningNode.Rank);

        Assert.Equal(2, topGainingRelationships.Rows.Count);
        Assert.Equal("KNOWS", topGainingRelationships.Rows[0].RelType);
        Assert.Equal("LIVES_IN", topGainingRelationships.Rows[1].RelType);

        var decliningRelationship = Assert.Single(topDecliningRelationships.Rows);
        Assert.Equal("WORKS_AT", decliningRelationship.RelType);
        Assert.Equal(-1, decliningRelationship.NetDelta);
        Assert.Equal(1, decliningRelationship.Rank);

        Assert.Equal(1, overview.AddedNodeCount);
        Assert.Equal(1, overview.RemovedNodeCount);
        Assert.Equal(2, overview.AddedRelCount);
        Assert.Equal(1, overview.RemovedRelCount);
        Assert.Equal(0, overview.NetNodeDelta);
        Assert.Equal(1, overview.NetRelDelta);
        Assert.Equal("nodes +1/-1, rels +2/-1", overview.SummaryLabel);

        Assert.Equal("nodes +1/-1, rels +2/-1", highlights.Overview.SummaryLabel);
        Assert.Single(highlights.TopGainingNodeTables.Rows);
        Assert.Single(highlights.TopDecliningNodeTables.Rows);
        Assert.Single(highlights.TopDecliningRelationshipTypes.Rows);
        Assert.Single(highlights.Review.TopChangedNodeTables.Rows);
    }

    [Fact]
    public void LocalRuntime_ChangeGroupSummaryAndDrilldown_ProjectStableSubsetFlows()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());
        var previousShard = new GraphShard
        {
            GraphVersionToken = "graph-v0",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice" },
                        new NodeShardRow { ExternalId = "node:Person:bob" }
                    ]
                },
                ["City"] = new()
                {
                    TableName = "City",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:City:portland" }
                    ]
                }
            },
            RelTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal)
            {
                ["KNOWS"] = new()
                {
                    RelType = "KNOWS",
                    FromTable = "Person",
                    ToTable = "Person",
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob" }
                    ]
                }
            }
        };

        var groupSummary = runtime.BuildChangeGroupSummary(previousShard);
        var personDrilldown = runtime.ProjectNodeTableChangeDrilldown(previousShard, "Person");
        var livesInDrilldown = runtime.ProjectRelationshipTypeChangeDrilldown(previousShard, "LIVES_IN");
        var reviewDrilldown = runtime.BuildChangeReviewDrilldown(previousShard, new GraphShardTopNSpec { Limit = 2 });

        Assert.Equal(2, groupSummary.ChangedNodeTableCount);
        Assert.Equal(1, groupSummary.GainingNodeTableCount);
        Assert.Equal(0, groupSummary.DecliningNodeTableCount);
        Assert.Equal(2, groupSummary.ChangedRelationshipTypeCount);
        Assert.Equal(2, groupSummary.GainingRelationshipTypeCount);
        Assert.Equal(0, groupSummary.DecliningRelationshipTypeCount);

        Assert.Equal("Person", personDrilldown.Key);
        Assert.Equal(1, personDrilldown.AddedCount);
        Assert.Equal(0, personDrilldown.RemovedCount);
        Assert.Equal("runtime_change_projection", personDrilldown.AddedProjection.ExtractionPolicy);
        Assert.Contains(personDrilldown.AddedProjection.NodeTables["Person"].Rows, row => row.ExternalId == "node:Person:carol");
        Assert.Contains(personDrilldown.AddedProjection.RelTables["KNOWS"].Rows, row => row.RelId == "rel:KNOWS:2");
        Assert.Empty(personDrilldown.RemovedProjection.NodeTables);

        Assert.Equal("LIVES_IN", livesInDrilldown.Key);
        Assert.Equal(1, livesInDrilldown.AddedCount);
        Assert.Equal(0, livesInDrilldown.RemovedCount);
        Assert.Contains(livesInDrilldown.AddedProjection.NodeTables["City"].Rows, row => row.ExternalId == "node:City:seattle");
        Assert.Contains(livesInDrilldown.AddedProjection.RelTables["LIVES_IN"].Rows, row => row.RelId == "rel:LIVES_IN:1");

        Assert.Equal(2, reviewDrilldown.NodeTableDrilldowns.Count);
        Assert.Equal(2, reviewDrilldown.RelationshipTypeDrilldowns.Count);
        Assert.Equal(2, reviewDrilldown.GroupSummary.ChangedNodeTableCount);
        Assert.Equal("Person", reviewDrilldown.NodeTableDrilldowns[0].Key);
        Assert.Equal("KNOWS", reviewDrilldown.RelationshipTypeDrilldowns[0].Key);
    }

    [Fact]
    public void LocalRuntime_MultiGroupReview_ComposesSelectedDrilldownsIntoStableMergedSubsets()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());
        var previousShard = new GraphShard
        {
            GraphVersionToken = "graph-v0",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice" },
                        new NodeShardRow { ExternalId = "node:Person:bob" }
                    ]
                },
                ["City"] = new()
                {
                    TableName = "City",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:City:portland" }
                    ]
                }
            },
            RelTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal)
            {
                ["KNOWS"] = new()
                {
                    RelType = "KNOWS",
                    FromTable = "Person",
                    ToTable = "Person",
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob" }
                    ]
                }
            }
        };

        var nodeComposition = runtime.ComposeNodeTableChangeDrilldowns(previousShard, ["City", "Person"]);
        var relationshipComposition = runtime.ComposeRelationshipTypeChangeDrilldowns(previousShard, ["KNOWS", "LIVES_IN"]);
        var selectedSummary = runtime.BuildSelectedChangeSummary(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"]);
        var multiReview = runtime.BuildMultiGroupChangeReview(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"], new GraphShardTopNSpec { Limit = 2 });

        Assert.Equal("node_tables", nodeComposition.Scope);
        Assert.Equal(["City", "Person"], nodeComposition.Keys);
        Assert.Equal(2, nodeComposition.Summary.SelectedNodeTableCount);
        Assert.Equal(4, nodeComposition.Summary.AddedNodeCount);
        Assert.Equal(1, nodeComposition.Summary.RemovedNodeCount);
        Assert.Contains(nodeComposition.AddedProjection.NodeTables["Person"].Rows, row => row.ExternalId == "node:Person:carol");
        Assert.Contains(nodeComposition.AddedProjection.NodeTables["City"].Rows, row => row.ExternalId == "node:City:seattle");
        Assert.Contains(nodeComposition.RemovedProjection.NodeTables["City"].Rows, row => row.ExternalId == "node:City:portland");

        Assert.Equal("relationship_types", relationshipComposition.Scope);
        Assert.Equal(["KNOWS", "LIVES_IN"], relationshipComposition.Keys);
        Assert.Equal(2, relationshipComposition.Summary.SelectedRelationshipTypeCount);
        Assert.Equal(2, relationshipComposition.Summary.AddedRelCount);
        Assert.Equal(0, relationshipComposition.Summary.RemovedRelCount);

        Assert.Equal(2, selectedSummary.SelectedNodeTableCount);
        Assert.Equal(2, selectedSummary.SelectedRelationshipTypeCount);
        Assert.Equal(4, selectedSummary.AddedNodeCount);
        Assert.Equal(1, selectedSummary.RemovedNodeCount);
        Assert.Equal(2, selectedSummary.AddedRelCount);

        Assert.Equal(2, multiReview.NodeTableComposition.Drilldowns.Count);
        Assert.Equal(2, multiReview.RelationshipTypeComposition.Drilldowns.Count);
        Assert.Equal(4, multiReview.SelectionSummary.AddedNodeCount);
        Assert.Equal(1, multiReview.SelectionSummary.RemovedNodeCount);
        Assert.Equal(2, multiReview.SelectionSummary.AddedRelCount);
        Assert.Equal("selected_groups", multiReview.CombinedComposition.Scope);
    }

    [Fact]
    public void LocalRuntime_CompareMultiGroupReviews_ComputesStableReviewOfReviewsDeltas()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());
        var previousShard = new GraphShard
        {
            GraphVersionToken = "graph-v0",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice" },
                        new NodeShardRow { ExternalId = "node:Person:bob" }
                    ]
                },
                ["City"] = new()
                {
                    TableName = "City",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:City:portland" }
                    ]
                }
            },
            RelTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal)
            {
                ["KNOWS"] = new()
                {
                    RelType = "KNOWS",
                    FromTable = "Person",
                    ToTable = "Person",
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob" }
                    ]
                }
            }
        };

        var narrowReview = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 });
        var wideReview = runtime.BuildMultiGroupChangeReview(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"], new GraphShardTopNSpec { Limit = 2 });
        var comparison = runtime.CompareMultiGroupChangeReviews(wideReview, narrowReview);

        Assert.Equal(1, comparison.SelectionSummaryDelta.SelectedNodeTableCountDelta);
        Assert.Equal(1, comparison.SelectionSummaryDelta.SelectedRelationshipTypeCountDelta);
        Assert.Equal(2, comparison.SelectionSummaryDelta.AddedNodeCountDelta);
        Assert.Equal(1, comparison.SelectionSummaryDelta.RemovedNodeCountDelta);
        Assert.Equal(1, comparison.SelectionSummaryDelta.AddedRelCountDelta);
        Assert.Equal(0, comparison.SelectionSummaryDelta.RemovedRelCountDelta);

        Assert.Equal("node_tables", comparison.NodeTableCompositionComparison.Scope);
        Assert.Equal(["City", "Person"], comparison.NodeTableCompositionComparison.CurrentKeys);
        Assert.Equal(["Person"], comparison.NodeTableCompositionComparison.PreviousKeys);
        Assert.Contains("selected nodeTables +1", comparison.CombinedCompositionComparison.SummaryDelta.SummaryLabel, StringComparison.Ordinal);
        Assert.Contains("node:City:seattle", comparison.CombinedCompositionComparison.AddedProjectionDelta.AddedNodeIds);
        Assert.Contains("node:City:portland", comparison.CombinedCompositionComparison.RemovedProjectionDelta.AddedNodeIds);
    }

    [Fact]
    public void LocalRuntime_CompareMultiGroupReviewSeries_ComputesBaselineSeriesSummary()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());
        var previousShard = new GraphShard
        {
            GraphVersionToken = "graph-v0",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice" },
                        new NodeShardRow { ExternalId = "node:Person:bob" }
                    ]
                },
                ["City"] = new()
                {
                    TableName = "City",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:City:portland" }
                    ]
                }
            },
            RelTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal)
            {
                ["KNOWS"] = new()
                {
                    RelType = "KNOWS",
                    FromTable = "Person",
                    ToTable = "Person",
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob" }
                    ]
                }
            }
        };

        var review0 = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 });
        var review1 = runtime.BuildMultiGroupChangeReview(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"], new GraphShardTopNSpec { Limit = 2 });
        var review2 = runtime.BuildMultiGroupChangeReview(previousShard, ["City"], ["LIVES_IN"], new GraphShardTopNSpec { Limit = 1 });
        var series = runtime.CompareMultiGroupChangeReviewSeries([review0, review1, review2], baselineIndex: 0);

        Assert.Equal(0, series.BaselineIndex);
        Assert.Equal(2, series.Rows.Count);
        Assert.Equal(1, series.Rows[0].Index);
        Assert.Equal("review[1]", series.Rows[0].Label);
        Assert.Equal(1, series.Rows[0].Comparison.SelectionSummaryDelta.SelectedNodeTableCountDelta);
        Assert.Equal(0, series.Rows[1].Comparison.SelectionSummaryDelta.SelectedNodeTableCountDelta);
        Assert.Equal(1, series.Summary.MaxAddedRelCountDelta);
        Assert.Equal(1, series.Summary.MaxRemovedNodeCountDelta);
        Assert.Contains("baseline review[0]", series.Summary.SummaryLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalRuntime_CompareNamedMultiGroupReviewSeries_OrdersByKeyAndUsesNamedBaseline()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());
        var previousShard = new GraphShard
        {
            GraphVersionToken = "graph-v0",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice" },
                        new NodeShardRow { ExternalId = "node:Person:bob" }
                    ]
                },
                ["City"] = new()
                {
                    TableName = "City",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:City:portland" }
                    ]
                }
            },
            RelTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal)
            {
                ["KNOWS"] = new()
                {
                    RelType = "KNOWS",
                    FromTable = "Person",
                    ToTable = "Person",
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob" }
                    ]
                }
            }
        };

        var reviews = new Dictionary<string, GraphShardMultiGroupChangeReview>(StringComparer.Ordinal)
        {
            ["wide"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"], new GraphShardTopNSpec { Limit = 2 }),
            ["city-only"] = runtime.BuildMultiGroupChangeReview(previousShard, ["City"], ["LIVES_IN"], new GraphShardTopNSpec { Limit = 1 }),
            ["person-only"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 })
        };

        var series = runtime.CompareNamedMultiGroupChangeReviewSeries(reviews, "person-only");

        Assert.Equal("person-only", series.BaselineKey);
        Assert.Equal(["city-only", "wide"], series.Rows.Select(row => row.Key).ToArray());
        Assert.Equal(2, series.Summary.ComparisonCount);
        Assert.Equal(1, series.Summary.MaxAddedRelCountDelta);
        Assert.Contains("baseline 'person-only'", series.Summary.SummaryLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalRuntime_CompareMultiGroupReviewMatrix_BuildsPairwiseComparisonCells()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());
        var previousShard = new GraphShard
        {
            GraphVersionToken = "graph-v0",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice" },
                        new NodeShardRow { ExternalId = "node:Person:bob" }
                    ]
                },
                ["City"] = new()
                {
                    TableName = "City",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:City:portland" }
                    ]
                }
            },
            RelTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal)
            {
                ["KNOWS"] = new()
                {
                    RelType = "KNOWS",
                    FromTable = "Person",
                    ToTable = "Person",
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob" }
                    ]
                }
            }
        };

        var reviews = new Dictionary<string, GraphShardMultiGroupChangeReview>(StringComparer.Ordinal)
        {
            ["wide"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"], new GraphShardTopNSpec { Limit = 2 }),
            ["city-only"] = runtime.BuildMultiGroupChangeReview(previousShard, ["City"], ["LIVES_IN"], new GraphShardTopNSpec { Limit = 1 }),
            ["person-only"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 })
        };

        var matrix = runtime.CompareMultiGroupChangeReviewMatrix(reviews);

        Assert.Equal(["city-only", "person-only", "wide"], matrix.Keys);
        Assert.Equal(6, matrix.Summary.ComparisonCount);
        Assert.Equal(3, matrix.Summary.ReviewCount);
        Assert.Contains("matrix across 3 review(s)", matrix.Summary.SummaryLabel, StringComparison.Ordinal);
        Assert.Contains(matrix.Cells, cell =>
            cell.CurrentKey == "wide" &&
            cell.PreviousKey == "person-only" &&
            cell.Comparison.SelectionSummaryDelta.SelectedRelationshipTypeCountDelta == 1);
    }

    [Fact]
    public void LocalRuntime_BuildMultiGroupReviewOverlap_ComputesCommonMembershipAcrossScopes()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());
        var previousShard = new GraphShard
        {
            GraphVersionToken = "graph-v0",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice" },
                        new NodeShardRow { ExternalId = "node:Person:bob" }
                    ]
                }
            },
            RelTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal)
            {
                ["KNOWS"] = new()
                {
                    RelType = "KNOWS",
                    FromTable = "Person",
                    ToTable = "Person",
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob" }
                    ]
                }
            }
        };

        var reviews = new Dictionary<string, GraphShardMultiGroupChangeReview>(StringComparer.Ordinal)
        {
            ["person"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 }),
            ["wide"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"], new GraphShardTopNSpec { Limit = 2 })
        };

        var overlap = runtime.BuildMultiGroupChangeReviewOverlap(reviews);
        var consensus = runtime.BuildMultiGroupChangeReviewConsensus(reviews);

        Assert.Equal(["person", "wide"], overlap.Keys);
        Assert.Equal(["Person"], overlap.CommonNodeTableKeys);
        Assert.Equal(["KNOWS"], overlap.CommonRelationshipTypeKeys);
        Assert.Equal(["node:Person:bob", "node:Person:carol"], overlap.CombinedCompositionOverlap.CommonAddedNodeIds);
        Assert.Equal(["rel:KNOWS:2"], overlap.CombinedCompositionOverlap.CommonAddedRelIds);
        Assert.Equal(2, consensus.ScopeCount);
        Assert.Equal(1, consensus.CommonSummary.SelectedNodeTableCount);
        Assert.Equal(1, consensus.CommonSummary.SelectedRelationshipTypeCount);
        Assert.Equal(2, consensus.CommonSummary.AddedNodeCount);
        Assert.Equal(1, consensus.CommonSummary.AddedRelCount);
        Assert.Contains("consensus across 2 scopes", consensus.CommonSummary.SummaryLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalRuntime_BuildMultiGroupReviewFrequencyAndThresholdConsensus_ComputesScopePresence()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());
        var previousShard = new GraphShard
        {
            GraphVersionToken = "graph-v0",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice" },
                        new NodeShardRow { ExternalId = "node:Person:bob" }
                    ]
                }
            },
            RelTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal)
            {
                ["KNOWS"] = new()
                {
                    RelType = "KNOWS",
                    FromTable = "Person",
                    ToTable = "Person",
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob" }
                    ]
                }
            }
        };

        var reviews = new Dictionary<string, GraphShardMultiGroupChangeReview>(StringComparer.Ordinal)
        {
            ["person"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 }),
            ["wide"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"], new GraphShardTopNSpec { Limit = 2 }),
            ["city"] = runtime.BuildMultiGroupChangeReview(previousShard, ["City"], ["LIVES_IN"], new GraphShardTopNSpec { Limit = 1 })
        };

        var frequency = runtime.BuildMultiGroupChangeReviewFrequency(reviews);
        var threshold = runtime.BuildMultiGroupChangeReviewThresholdConsensus(reviews, 2);

        Assert.Equal(3, frequency.ScopeCount);
        Assert.Equal("City", frequency.NodeTableKeys[0].Key);
        Assert.Equal(2, frequency.NodeTableKeys[0].ScopeCount);
        Assert.Equal("Person", frequency.NodeTableKeys[1].Key);
        Assert.Equal(2, frequency.NodeTableKeys[1].ScopeCount);
        Assert.Equal("KNOWS", frequency.RelationshipTypeKeys[0].Key);
        Assert.Equal(2, frequency.RelationshipTypeKeys[0].ScopeCount);
        Assert.Equal(["City", "Person"], threshold.QualifiedNodeTableKeys);
        Assert.Equal(["KNOWS", "LIVES_IN"], threshold.QualifiedRelationshipTypeKeys);
        Assert.Equal(["node:City:seattle", "node:Person:alice", "node:Person:bob", "node:Person:carol"], threshold.ThresholdProjectionOverlap.CommonAddedNodeIds);
        Assert.Equal(2, threshold.ThresholdSummary.SelectedNodeTableCount);
        Assert.Equal(2, threshold.ThresholdSummary.SelectedRelationshipTypeCount);
        Assert.Contains(">= 2", threshold.ThresholdSummary.SummaryLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalRuntime_BuildSelectionProfilesAndFamilies_ComputesStableScopeSignatures()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());
        var previousShard = new GraphShard
        {
            GraphVersionToken = "graph-v0",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice" },
                        new NodeShardRow { ExternalId = "node:Person:bob" }
                    ]
                }
            },
            RelTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal)
            {
                ["KNOWS"] = new()
                {
                    RelType = "KNOWS",
                    FromTable = "Person",
                    ToTable = "Person",
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob" }
                    ]
                }
            }
        };

        var reviews = new Dictionary<string, GraphShardMultiGroupChangeReview>(StringComparer.Ordinal)
        {
            ["person-a"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 }),
            ["wide"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"], new GraphShardTopNSpec { Limit = 2 }),
            ["person-b"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 })
        };

        var profiles = runtime.BuildMultiGroupChangeReviewSelectionProfiles(reviews);
        var families = runtime.BuildMultiGroupChangeReviewSelectionFamilies(reviews);

        Assert.Equal(["person-a", "person-b", "wide"], profiles.Keys);
        Assert.Equal("nodes[Person]|rels[KNOWS]", profiles.Profiles[0].Signature);
        Assert.Equal("nodes[Person]|rels[KNOWS]", profiles.Profiles[1].Signature);
        Assert.Equal("nodes[City,Person]|rels[KNOWS,LIVES_IN]", profiles.Profiles[2].Signature);
        Assert.Equal(2, families.Families.Count);
        Assert.Contains(families.Families, family =>
            family.Signature == "nodes[Person]|rels[KNOWS]" &&
            family.ScopeCount == 2 &&
            family.Keys.SequenceEqual(["person-a", "person-b"]));
    }

    [Fact]
    public void LocalRuntime_CompareSelectionProfilesAndFamilies_ComputesStableShapeDeltas()
    {
        var currentProfile = new GraphShardMultiGroupChangeReviewSelectionProfile
        {
            Key = "wide",
            SelectedNodeTableKeys = ["City", "Person"],
            SelectedRelationshipTypeKeys = ["KNOWS", "LIVES_IN"],
            SelectedNodeTableCount = 2,
            SelectedRelationshipTypeCount = 2,
            Signature = "nodes[City,Person]|rels[KNOWS,LIVES_IN]"
        };
        var previousProfile = new GraphShardMultiGroupChangeReviewSelectionProfile
        {
            Key = "person",
            SelectedNodeTableKeys = ["Person"],
            SelectedRelationshipTypeKeys = ["KNOWS"],
            SelectedNodeTableCount = 1,
            SelectedRelationshipTypeCount = 1,
            Signature = "nodes[Person]|rels[KNOWS]"
        };

        var profileDelta = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard()).CompareSelectionProfiles(currentProfile, previousProfile);
        var currentFamilies = new GraphShardMultiGroupChangeReviewSelectionFamilies
        {
            Keys = ["person-a", "person-b", "wide"],
            Families =
            [
                new GraphShardMultiGroupChangeReviewSelectionFamily
                {
                    Signature = "nodes[Person]|rels[KNOWS]",
                    Keys = ["person-a", "person-b"],
                    SelectedNodeTableKeys = ["Person"],
                    SelectedRelationshipTypeKeys = ["KNOWS"],
                    ScopeCount = 2
                },
                new GraphShardMultiGroupChangeReviewSelectionFamily
                {
                    Signature = "nodes[City,Person]|rels[KNOWS,LIVES_IN]",
                    Keys = ["wide"],
                    SelectedNodeTableKeys = ["City", "Person"],
                    SelectedRelationshipTypeKeys = ["KNOWS", "LIVES_IN"],
                    ScopeCount = 1
                }
            ]
        };
        var previousFamilies = new GraphShardMultiGroupChangeReviewSelectionFamilies
        {
            Keys = ["person-a", "person-b", "city"],
            Families =
            [
                new GraphShardMultiGroupChangeReviewSelectionFamily
                {
                    Signature = "nodes[Person]|rels[KNOWS]",
                    Keys = ["person-a", "person-b"],
                    SelectedNodeTableKeys = ["Person"],
                    SelectedRelationshipTypeKeys = ["KNOWS"],
                    ScopeCount = 2
                },
                new GraphShardMultiGroupChangeReviewSelectionFamily
                {
                    Signature = "nodes[City]|rels[LIVES_IN]",
                    Keys = ["city"],
                    SelectedNodeTableKeys = ["City"],
                    SelectedRelationshipTypeKeys = ["LIVES_IN"],
                    ScopeCount = 1
                }
            ]
        };

        var familyDelta = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard()).CompareSelectionFamilies(currentFamilies, previousFamilies);

        Assert.Equal(["City"], profileDelta.AddedNodeTableKeys);
        Assert.Empty(profileDelta.RemovedNodeTableKeys);
        Assert.Equal(["LIVES_IN"], profileDelta.AddedRelationshipTypeKeys);
        Assert.Contains("nodeTables +1/-0", profileDelta.SummaryLabel, StringComparison.Ordinal);
        Assert.Contains(familyDelta.Rows, row =>
            row.Signature == "nodes[City,Person]|rels[KNOWS,LIVES_IN]" &&
            row.ScopeCountDelta == 1);
        Assert.Contains(familyDelta.Rows, row =>
            row.Signature == "nodes[City]|rels[LIVES_IN]" &&
            row.ScopeCountDelta == -1);
    }

    [Fact]
    public void LocalRuntime_BuildAndSummarizeSelectionSignatureTransitions_ComputesKeyedSignatureChanges()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());
        var currentProfiles = new GraphShardMultiGroupChangeReviewSelectionProfiles
        {
            Keys = ["city", "person", "wide"],
            Profiles =
            [
                new GraphShardMultiGroupChangeReviewSelectionProfile
                {
                    Key = "city",
                    SelectedNodeTableKeys = ["City"],
                    SelectedRelationshipTypeKeys = ["LIVES_IN"],
                    Signature = "nodes[City]|rels[LIVES_IN]"
                },
                new GraphShardMultiGroupChangeReviewSelectionProfile
                {
                    Key = "person",
                    SelectedNodeTableKeys = ["Person"],
                    SelectedRelationshipTypeKeys = ["KNOWS"],
                    Signature = "nodes[Person]|rels[KNOWS]"
                },
                new GraphShardMultiGroupChangeReviewSelectionProfile
                {
                    Key = "wide",
                    SelectedNodeTableKeys = ["City", "Person"],
                    SelectedRelationshipTypeKeys = ["KNOWS", "LIVES_IN"],
                    Signature = "nodes[City,Person]|rels[KNOWS,LIVES_IN]"
                }
            ]
        };
        var previousProfiles = new GraphShardMultiGroupChangeReviewSelectionProfiles
        {
            Keys = ["person", "wide"],
            Profiles =
            [
                new GraphShardMultiGroupChangeReviewSelectionProfile
                {
                    Key = "person",
                    SelectedNodeTableKeys = ["Person"],
                    SelectedRelationshipTypeKeys = ["KNOWS"],
                    Signature = "nodes[Person]|rels[KNOWS]"
                },
                new GraphShardMultiGroupChangeReviewSelectionProfile
                {
                    Key = "wide",
                    SelectedNodeTableKeys = ["City"],
                    SelectedRelationshipTypeKeys = ["LIVES_IN"],
                    Signature = "nodes[City]|rels[LIVES_IN]"
                }
            ]
        };

        var transitions = runtime.BuildSelectionSignatureTransitions(currentProfiles, previousProfiles);
        var summary = runtime.SummarizeSelectionSignatureTransitions(transitions);

        Assert.Equal(["city", "person", "wide"], transitions.Keys);
        Assert.Equal(2, transitions.ChangedScopeCount);
        Assert.Equal(1, transitions.UnchangedScopeCount);
        Assert.Contains(transitions.Rows, row =>
            row.Key == "city" &&
            row.PreviousSignature == "<missing>" &&
            row.CurrentSignature == "nodes[City]|rels[LIVES_IN]" &&
            row.Changed);
        Assert.Contains(transitions.Rows, row =>
            row.Key == "person" &&
            row.PreviousSignature == "nodes[Person]|rels[KNOWS]" &&
            row.CurrentSignature == "nodes[Person]|rels[KNOWS]" &&
            !row.Changed);
        Assert.Contains(summary.Rows, row =>
            row.PreviousSignature == "<missing>" &&
            row.CurrentSignature == "nodes[City]|rels[LIVES_IN]" &&
            row.ScopeCount == 1);
        Assert.Contains(summary.Rows, row =>
            row.PreviousSignature == "nodes[City]|rels[LIVES_IN]" &&
            row.CurrentSignature == "nodes[City,Person]|rels[KNOWS,LIVES_IN]" &&
            row.ScopeCount == 1);
    }

    [Fact]
    public void LocalRuntime_TopSummaryHelpers_ReturnBoundedRowsWithoutClientResorting()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var neighborSummary = runtime.SummarizeNeighbors(
            "node:Person:alice",
            new GraphShardNodeFilterSpec
            {
                Composition = GraphShardFilterComposition.Any,
                Children =
                [
                    new GraphShardNodeFilterSpec
                    {
                        Predicate = new GraphShardNodePredicateSpec
                        {
                            TableNames = ["Person"],
                            PropertyName = "age",
                            Operator = GraphShardPredicateOperator.GreaterThanOrEqual,
                            Value = 28
                        }
                    },
                    new GraphShardNodeFilterSpec
                    {
                        Predicate = new GraphShardNodePredicateSpec
                        {
                            TableNames = ["City"],
                            PropertyName = "name",
                            Operator = GraphShardPredicateOperator.Equal,
                            Value = "Seattle"
                        }
                    }
                ]
            },
            includeOutgoing: true,
            includeIncoming: false,
            relType: null,
            new GraphShardAggregateSpec { Key = "neighborAgeSum", Function = GraphShardAggregateFunction.Sum, PropertyName = "age", Source = GraphShardAggregateSource.Node });

        var relationshipSummary = runtime.SummarizeRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Composition = GraphShardFilterComposition.Any,
                Children =
                [
                    new GraphShardRelationshipFilterSpec
                    {
                        Predicate = new GraphShardRelationshipPredicateSpec
                        {
                            RelTypes = ["KNOWS"],
                            PropertyName = "weight",
                            Operator = GraphShardPredicateOperator.Exists
                        }
                    },
                    new GraphShardRelationshipFilterSpec
                    {
                        Predicate = new GraphShardRelationshipPredicateSpec
                        {
                            RelTypes = ["LIVES_IN"],
                            PropertyName = "since",
                            Operator = GraphShardPredicateOperator.Exists
                        }
                    }
                ]
            },
            new GraphShardAggregateSpec { Key = "metricSum", Function = GraphShardAggregateFunction.Sum, PropertyName = "weight", Source = GraphShardAggregateSource.Relationship });

        var topNeighbors = runtime.TopNeighborSummaries(
            neighborSummary,
            new GraphShardTopNSpec
            {
                Limit = 1,
                AggregateKey = "neighborAgeSum",
                Direction = GraphShardSortDirection.Desc
            });
        var topRelationships = runtime.TopRelationshipSummaries(
            relationshipSummary,
            new GraphShardTopNSpec
            {
                Limit = 1
            });

        var neighbor = Assert.Single(topNeighbors.Rows);
        Assert.Equal("KNOWS", neighbor.RelType);
        Assert.Equal("Person", neighbor.TargetTable);

        var relationship = Assert.Single(topRelationships.Rows);
        Assert.Equal("KNOWS", relationship.RelType);
        Assert.Equal("Person", relationship.SourceTable);
        Assert.Equal("Person", relationship.TargetTable);
    }

    [Fact]
    public void LocalRuntime_HistogramBucketFilters_ComposeIntoReusableRangeFilters()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var nodeHistogram = runtime.HistogramNodes(
            new GraphShardNodeFilterSpec
            {
                Predicate = new GraphShardNodePredicateSpec
                {
                    TableNames = ["Person"],
                    PropertyName = "age",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            "age",
            5m);
        var relationshipHistogram = runtime.HistogramRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    RelTypes = ["KNOWS"],
                    PropertyName = "weight",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            "weight",
            2m);

        var nodeFilter = runtime.CreateNodeHistogramBucketFilter(
            "age",
            nodeHistogram.Buckets[0],
            new GraphShardNodeFilterSpec
            {
                Predicate = new GraphShardNodePredicateSpec
                {
                    TableNames = ["Person"],
                    PropertyName = "age",
                    Operator = GraphShardPredicateOperator.Exists
                }
            });
        var relationshipFilter = runtime.CreateRelationshipHistogramBucketFilter(
            "weight",
            relationshipHistogram.Buckets[1],
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    RelTypes = ["KNOWS"],
                    PropertyName = "weight",
                    Operator = GraphShardPredicateOperator.Exists
                }
            });

        var nodeMatches = runtime.FilterNodes(nodeFilter);
        var relationshipMatches = runtime.FilterRelationships(relationshipFilter);

        Assert.Equal(
            ["node:Person:bob", "node:Person:carol"],
            nodeMatches.Select(match => match.Row.ExternalId).OrderBy(x => x));
        Assert.Equal(["rel:KNOWS:1"], relationshipMatches.Select(match => match.Row.RelId));
    }

    [Fact]
    public void LocalRuntime_HistogramBucketDrillDown_ProjectsSubgraphsAndSummaries()
    {
        var runtime = GraphShardLocalRuntime.LoadExtract(CreateProjectionShard());

        var nodeHistogram = runtime.HistogramNodes(
            new GraphShardNodeFilterSpec
            {
                Predicate = new GraphShardNodePredicateSpec
                {
                    TableNames = ["Person"],
                    PropertyName = "age",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            "age",
            5m);
        var relHistogram = runtime.HistogramRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    RelTypes = ["KNOWS"],
                    PropertyName = "weight",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            "weight",
            2m);

        var nodeProjection = runtime.ProjectNodeHistogramBucketSubgraph(
            "age",
            nodeHistogram.Buckets[0],
            new GraphShardNodeFilterSpec
            {
                Predicate = new GraphShardNodePredicateSpec
                {
                    TableNames = ["Person"],
                    PropertyName = "age",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            includeOutgoing: true,
            includeIncoming: true,
            relType: "KNOWS");
        var relationshipProjection = runtime.ProjectRelationshipHistogramBucketSubgraph(
            "weight",
            relHistogram.Buckets[1],
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    RelTypes = ["KNOWS"],
                    PropertyName = "weight",
                    Operator = GraphShardPredicateOperator.Exists
                }
            });
        var nodeSummary = runtime.SummarizeNeighborsForNodeHistogramBucket(
            "node:Person:alice",
            "age",
            nodeHistogram.Buckets[0],
            new GraphShardNodeFilterSpec
            {
                Predicate = new GraphShardNodePredicateSpec
                {
                    TableNames = ["Person"],
                    PropertyName = "age",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            includeOutgoing: true,
            includeIncoming: false,
            relType: "KNOWS");
        var relationshipSummary = runtime.SummarizeRelationshipsForHistogramBucket(
            "weight",
            relHistogram.Buckets[1],
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    RelTypes = ["KNOWS"],
                    PropertyName = "weight",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            new GraphShardAggregateSpec { Key = "count", Function = GraphShardAggregateFunction.Count, Source = GraphShardAggregateSource.Relationship });

        Assert.Equal(2, nodeProjection.Stats.NodeCount);
        Assert.Equal(1, nodeProjection.Stats.EdgeCount);
        Assert.Equal(2, relationshipProjection.Stats.NodeCount);
        Assert.Equal(1, relationshipProjection.Stats.EdgeCount);

        var summaryRow = Assert.Single(nodeSummary.Rows);
        Assert.Equal("KNOWS", summaryRow.RelType);
        Assert.Equal("Person", summaryRow.TargetTable);
        Assert.Equal(1, summaryRow.Count);

        var relationshipRow = Assert.Single(relationshipSummary.Rows);
        Assert.Equal("KNOWS", relationshipRow.RelType);
        Assert.Equal(1, relationshipRow.Count);
    }

    private static GraphShard CreateProjectionShard()
        => new()
        {
            GraphVersionToken = "graph-v1",
            ExtractionPolicy = "extract_neighborhood",
            IsComplete = true,
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    PropertyColumns = ["name"],
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice", Properties = new Dictionary<string, object?> { ["name"] = "Alice" } },
                        new NodeShardRow { ExternalId = "node:Person:bob", Properties = new Dictionary<string, object?> { ["name"] = "Bob", ["age"] = 28, ["joinedAt"] = "2024-01-15T10:00:00Z" } },
                        new NodeShardRow { ExternalId = "node:Person:carol", Properties = new Dictionary<string, object?> { ["name"] = "Carol", ["age"] = 26, ["joinedAt"] = "2024-02-02T08:30:00Z" } }
                    ]
                },
                ["City"] = new()
                {
                    TableName = "City",
                    PropertyColumns = ["name"],
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:City:seattle", Properties = new Dictionary<string, object?> { ["name"] = "Seattle" } }
                    ]
                }
            },
            RelTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal)
            {
                ["KNOWS"] = new()
                {
                    RelType = "KNOWS",
                    FromTable = "Person",
                    ToTable = "Person",
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob", Properties = new Dictionary<string, object?> { ["weight"] = 4, ["recordedAt"] = "2024-01-15T11:00:00Z" } },
                        new RelShardRow { RelId = "rel:KNOWS:2", SourceNodeId = "node:Person:bob", TargetNodeId = "node:Person:carol", Properties = new Dictionary<string, object?> { ["weight"] = 3, ["recordedAt"] = "2024-01-16T12:00:00Z" } }
                    ]
                },
                ["LIVES_IN"] = new()
                {
                    RelType = "LIVES_IN",
                    FromTable = "Person",
                    ToTable = "City",
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:LIVES_IN:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:City:seattle", Properties = new Dictionary<string, object?> { ["since"] = 2019 } }
                    ]
                }
            },
            Adjacency = new GraphShardAdjacency
            {
                Outgoing = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal)
                {
                    ["node:Person:alice"] =
                    [
                        new ShardEdgeRef { RelId = "rel:KNOWS:1", RelType = "KNOWS", NeighborNodeId = "node:Person:bob", Direction = "out" },
                        new ShardEdgeRef { RelId = "rel:LIVES_IN:1", RelType = "LIVES_IN", NeighborNodeId = "node:City:seattle", Direction = "out" }
                    ],
                    ["node:Person:bob"] =
                    [
                        new ShardEdgeRef { RelId = "rel:KNOWS:2", RelType = "KNOWS", NeighborNodeId = "node:Person:carol", Direction = "out" }
                    ]
                },
                Incoming = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal)
                {
                    ["node:Person:bob"] =
                    [
                        new ShardEdgeRef { RelId = "rel:KNOWS:1", RelType = "KNOWS", NeighborNodeId = "node:Person:alice", Direction = "in" }
                    ],
                    ["node:Person:carol"] =
                    [
                        new ShardEdgeRef { RelId = "rel:KNOWS:2", RelType = "KNOWS", NeighborNodeId = "node:Person:bob", Direction = "in" }
                    ],
                    ["node:City:seattle"] =
                    [
                        new ShardEdgeRef { RelId = "rel:LIVES_IN:1", RelType = "LIVES_IN", NeighborNodeId = "node:Person:alice", Direction = "in" }
                    ]
                }
            },
            Stats = new GraphShardStats
            {
                NodeCount = 4,
                EdgeCount = 3,
                NodeTableCount = 2,
                RelTableCount = 2
            }
        };
}
