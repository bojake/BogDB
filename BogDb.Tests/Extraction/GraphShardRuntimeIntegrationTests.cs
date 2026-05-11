using BogDb.Core.Extraction;
using Xunit;

namespace BogDb.Tests.Extraction;

public sealed class GraphShardRuntimeIntegrationTests
{
    [Fact]
    public void ExtractMergeRuntimeFlow_ComposesPartialNeighborhoods()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var aliceShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });
        var bobShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(aliceShard);
        runtime.MergeExtract(bobShard);

        Assert.True(runtime.HasNode("node:Person:alice"));
        Assert.True(runtime.HasNode("node:Person:bob"));
        Assert.True(runtime.HasNode("node:Person:carol"));
        Assert.Equal(["node:City:seattle", "node:Person:bob"], runtime.Expand("node:Person:alice", includeOutgoing: true, includeIncoming: false));
        Assert.Equal(4, runtime.Shard.Stats.NodeCount);
        Assert.Equal(3, runtime.Shard.Stats.EdgeCount);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_ProjectNeighborhood_ProducesSubgraphFromMergedState()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var aliceShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });
        var bobShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(GraphShardMerger.Merge(aliceShard, bobShard));
        var projected = runtime.ProjectNeighborhood(
            "node:Person:alice",
            depth: 2,
            includeOutgoing: true,
            includeIncoming: false);

        Assert.Equal("runtime_projection", projected.ExtractionPolicy);
        Assert.True(projected.NodeTables.ContainsKey("Person"));
        Assert.Contains(projected.NodeTables["Person"].Rows, row => row.ExternalId == "node:Person:alice");
        Assert.Contains(projected.NodeTables["Person"].Rows, row => row.ExternalId == "node:Person:carol");
        Assert.Single(projected.RelTables["KNOWS"].Rows, row => row.SourceNodeId == "node:Person:alice");
        Assert.Single(projected.RelTables["KNOWS"].Rows, row => row.SourceNodeId == "node:Person:bob");
        Assert.Equal(4, projected.Stats.NodeCount);
        Assert.Equal(3, projected.Stats.EdgeCount);
        Assert.False(projected.IsComplete);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_FilterAndPath_WorkAgainstMergedExtracts()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var aliceShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });
        var bobShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(GraphShardMerger.Merge(aliceShard, bobShard));

        var filtered = runtime.FilterNodes(new GraphShardNodePredicateSpec
        {
            TableName = "Person",
            PropertyName = "age",
            Operator = GraphShardPredicateOperator.GreaterThanOrEqual,
            Value = 28
        });

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

        Assert.Equal(
            ["node:Person:alice", "node:Person:bob"],
            filtered.Select(match => match.Row.ExternalId).OrderBy(x => x));
        Assert.NotNull(path);
        Assert.Equal(
            ["node:Person:alice", "node:Person:bob", "node:Person:carol"],
            path!.NodeIds);
        Assert.Equal(2, path.Length);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_ProjectPath_AndSelectorFilter_WorkAgainstMergedExtracts()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var aliceShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });
        var bobShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(GraphShardMerger.Merge(aliceShard, bobShard));

        var filtered = runtime.FilterNodes(new GraphShardNodePredicateSpec
        {
            TableNames = ["Person"],
            ExternalIds = ["node:Person:alice", "node:Person:bob", "node:Person:carol"],
            PropertyName = "name",
            Operator = GraphShardPredicateOperator.In,
            Values = ["Alice", "Carol"]
        });

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

        Assert.Equal(
            ["node:Person:alice", "node:Person:carol"],
            filtered.Select(match => match.Row.ExternalId).OrderBy(x => x));
        Assert.NotNull(projected);
        Assert.Equal(3, projected!.Shard.Stats.NodeCount);
        Assert.Equal(2, projected.Shard.Stats.EdgeCount);
        Assert.True(projected.Shard.RelTables.ContainsKey("KNOWS"));
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_BooleanFilterAndProjectedPathMetadata_WorkAgainstMergedExtracts()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var aliceShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });
        var bobShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(GraphShardMerger.Merge(aliceShard, bobShard));

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

        var filtered = runtime.FilterNodes(filter);
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

        Assert.Equal(
            ["node:City:seattle", "node:Person:bob"],
            filtered.Select(match => match.Row.ExternalId).OrderBy(x => x));
        Assert.NotNull(projected);
        Assert.Equal("node:Person:alice", projected!.StartNode.Row.ExternalId);
        Assert.Equal("node:Person:carol", projected.EndNode.Row.ExternalId);
        Assert.Equal(2, projected.Relationships.Count);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_PathShapeAndFilteredProjection_WorkAgainstMergedExtracts()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var aliceShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });
        var bobShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(GraphShardMerger.Merge(aliceShard, bobShard));

        var path = runtime.ProjectPath(
            "node:Person:alice",
            "node:Person:carol",
            new GraphShardPathOptions
            {
                IncludeOutgoing = true,
                IncludeIncoming = false,
                RelType = "KNOWS",
                MaxDepth = 3
            });

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

        Assert.NotNull(path);
        Assert.Equal(["Person", "Person", "Person"], path!.NodeTableSequence);
        Assert.Equal(["KNOWS", "KNOWS"], path.RelTypeSequence);
        Assert.Equal(3, projected.Stats.NodeCount);
        Assert.Equal(2, projected.Stats.EdgeCount);
        Assert.Equal("runtime_filter_projection", projected.ExtractionPolicy);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_RelationshipFilteringAndProjection_WorkAgainstMergedExtracts()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var aliceShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });
        var bobShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(GraphShardMerger.Merge(aliceShard, bobShard));

        var relationships = runtime.FilterRelationships(new GraphShardRelationshipFilterSpec
        {
            Predicate = new GraphShardRelationshipPredicateSpec
            {
                RelTypes = ["KNOWS"],
                PropertyName = "since",
                Operator = GraphShardPredicateOperator.GreaterThanOrEqual,
                Value = 2021
            }
        });

        var projected = runtime.ProjectFilteredRelationships(new GraphShardRelationshipFilterSpec
        {
            Predicate = new GraphShardRelationshipPredicateSpec
            {
                RelTypes = ["KNOWS"],
                PropertyName = "since",
                Operator = GraphShardPredicateOperator.GreaterThanOrEqual,
                Value = 2021
            }
        });

        Assert.Single(relationships);
        Assert.Equal(2021L, relationships[0].Row.Properties["since"]);
        Assert.Equal("runtime_relationship_projection", projected.ExtractionPolicy);
        Assert.Equal(
            ["node:Person:bob", "node:Person:carol"],
            projected.NodeTables["Person"].Rows.Select(row => row.ExternalId).OrderBy(x => x));
        Assert.Equal(2, projected.Stats.NodeCount);
        Assert.Equal(1, projected.Stats.EdgeCount);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_Aggregates_WorkAgainstMergedExtracts()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var aliceShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });
        var bobShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(GraphShardMerger.Merge(aliceShard, bobShard));

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
            groupByProperty: null,
            new GraphShardAggregateSpec { Key = "count", Function = GraphShardAggregateFunction.Count },
            new GraphShardAggregateSpec { Key = "ageSum", Function = GraphShardAggregateFunction.Sum, PropertyName = "age" });

        var relAggregate = runtime.AggregateRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    RelTypes = ["KNOWS"],
                    PropertyName = "since",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            groupByProperty: "relType",
            new GraphShardAggregateSpec { Key = "count", Function = GraphShardAggregateFunction.Count },
            new GraphShardAggregateSpec { Key = "sinceSum", Function = GraphShardAggregateFunction.Sum, PropertyName = "since" });

        Assert.Single(nodeAggregate.Rows);
        Assert.Equal("__all__", nodeAggregate.Rows[0].GroupKey);
        Assert.Equal(3, Convert.ToInt32(nodeAggregate.Rows[0].Values["count"]));
        Assert.Equal(84m, Convert.ToDecimal(nodeAggregate.Rows[0].Values["ageSum"]));
        Assert.Single(relAggregate.Rows);
        Assert.Equal("KNOWS", relAggregate.Rows[0].GroupKey);
        Assert.Equal(2, Convert.ToInt32(relAggregate.Rows[0].Values["count"]));
        Assert.Equal(4041m, Convert.ToDecimal(relAggregate.Rows[0].Values["sinceSum"]));
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_SortPageAndNeighborSummary_WorkAgainstMergedExtracts()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var aliceShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });
        var bobShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(GraphShardMerger.Merge(aliceShard, bobShard));

        var sortedNodes = runtime.SortAndPageNodes(
            runtime.FilterNodes(new GraphShardNodeFilterSpec
            {
                Predicate = new GraphShardNodePredicateSpec
                {
                    TableNames = ["Person"],
                    PropertyName = "age",
                    Operator = GraphShardPredicateOperator.Exists
                }
            }),
            new GraphShardNodeSortSpec { PropertyName = "age", Direction = GraphShardSortDirection.Desc },
            new GraphShardPageSpec { Offset = 0, Limit = 2 });

        var sortedRelationships = runtime.SortAndPageRelationships(
            runtime.FilterRelationships(new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    RelTypes = ["KNOWS"],
                    PropertyName = "since",
                    Operator = GraphShardPredicateOperator.Exists
                }
            }),
            new GraphShardRelationshipSortSpec { PropertyName = "since", Direction = GraphShardSortDirection.Desc },
            new GraphShardPageSpec { Offset = 0, Limit = 1 });

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

        Assert.Equal(["node:Person:alice", "node:Person:bob"], sortedNodes.Select(match => match.Row.ExternalId));
        Assert.Single(sortedRelationships);
        Assert.Equal(2021L, sortedRelationships[0].Row.Properties["since"]);
        Assert.Equal(2, summary.Rows.Count);
        Assert.Contains(summary.Rows, row => row.RelType == "KNOWS" && row.TargetTable == "Person" && row.Count == 1);
        Assert.Contains(summary.Rows, row => row.RelType == "LIVES_IN" && row.TargetTable == "City" && row.Count == 1);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_CursorPagingAndSummaryAggregates_WorkAgainstMergedExtracts()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var aliceShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });
        var bobShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(GraphShardMerger.Merge(aliceShard, bobShard));

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
            new GraphShardCursorPageSpec { Limit = 2 });
        var secondNodePage = runtime.CursorPageNodes(
            nodeMatches,
            [
                new GraphShardNodeSortSpec { PropertyName = "age", Direction = GraphShardSortDirection.Desc },
                new GraphShardNodeSortSpec { PropertyName = "name", Direction = GraphShardSortDirection.Asc }
            ],
            new GraphShardCursorPageSpec { Limit = 2, AfterCursor = firstNodePage.NextCursor });

        var relMatches = runtime.FilterRelationships(new GraphShardRelationshipFilterSpec
        {
            Predicate = new GraphShardRelationshipPredicateSpec
            {
                RelTypes = ["KNOWS"],
                PropertyName = "since",
                Operator = GraphShardPredicateOperator.Exists
            }
        });
        var firstRelPage = runtime.CursorPageRelationships(
            relMatches,
            [
                new GraphShardRelationshipSortSpec { PropertyName = "since", Direction = GraphShardSortDirection.Desc },
                new GraphShardRelationshipSortSpec { PropertyName = "relType", Direction = GraphShardSortDirection.Asc }
            ],
            new GraphShardCursorPageSpec { Limit = 1 });
        var secondRelPage = runtime.CursorPageRelationships(
            relMatches,
            [
                new GraphShardRelationshipSortSpec { PropertyName = "since", Direction = GraphShardSortDirection.Desc },
                new GraphShardRelationshipSortSpec { PropertyName = "relType", Direction = GraphShardSortDirection.Asc }
            ],
            new GraphShardCursorPageSpec { Limit = 1, AfterCursor = firstRelPage.NextCursor });

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

        Assert.Equal(["node:Person:alice", "node:Person:bob"], firstNodePage.Items.Select(match => match.Row.ExternalId));
        Assert.Equal(["node:Person:carol"], secondNodePage.Items.Select(match => match.Row.ExternalId));
        Assert.Equal(2021L, firstRelPage.Items[0].Row.Properties["since"]);
        Assert.Equal(2020L, secondRelPage.Items[0].Row.Properties["since"]);

        var knows = Assert.Single(summary.Rows.Where(row => row.RelType == "KNOWS"));
        var livesIn = Assert.Single(summary.Rows.Where(row => row.RelType == "LIVES_IN"));
        Assert.Equal(28m, Convert.ToDecimal(knows.AggregateValues["neighborAgeSum"]));
        Assert.Equal(2019m, Convert.ToDecimal(livesIn.AggregateValues["relationshipSinceSum"]));
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_RelationshipSummaryAndHistograms_WorkAgainstMergedExtracts()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var aliceShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });
        var bobShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(GraphShardMerger.Merge(aliceShard, bobShard));

        var summary = runtime.SummarizeRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    RelTypes = ["KNOWS"],
                    PropertyName = "since",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            new GraphShardAggregateSpec { Key = "count", Function = GraphShardAggregateFunction.Count, Source = GraphShardAggregateSource.Relationship },
            new GraphShardAggregateSpec { Key = "sinceSum", Function = GraphShardAggregateFunction.Sum, PropertyName = "since", Source = GraphShardAggregateSource.Relationship });

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
                    PropertyName = "since",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            "since",
            1m);

        var knows = Assert.Single(summary.Rows);
        Assert.Equal("KNOWS", knows.RelType);
        Assert.Equal("Person", knows.SourceTable);
        Assert.Equal("Person", knows.TargetTable);
        Assert.Equal(2, knows.Count);
        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(1m, knows.Share);
        Assert.Equal(4041m, Convert.ToDecimal(knows.AggregateValues["sinceSum"]));

        Assert.Equal(2, nodeHistogram.Buckets.Count);
        Assert.Equal(3, nodeHistogram.TotalCount);
        Assert.Equal(25m, nodeHistogram.Buckets[0].StartInclusive);
        Assert.Equal(2, nodeHistogram.Buckets[0].Count);
        Assert.Equal(2m / 3m, nodeHistogram.Buckets[0].Share);

        Assert.Equal(2, relHistogram.Buckets.Count);
        Assert.Equal(2, relHistogram.TotalCount);
        Assert.Equal(2020m, relHistogram.Buckets[0].StartInclusive);
        Assert.Equal(1, relHistogram.Buckets[0].Count);
        Assert.Equal(0.5m, relHistogram.Buckets[0].Share);
        Assert.Equal(2021m, relHistogram.Buckets[1].StartInclusive);
        Assert.Equal(1, relHistogram.Buckets[1].Count);
        Assert.Equal(0.5m, relHistogram.Buckets[1].Share);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_TopSummariesAndBucketFilters_WorkAgainstMergedExtracts()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var aliceShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });
        var bobShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(GraphShardMerger.Merge(aliceShard, bobShard));

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

        var topNeighborSummary = runtime.TopNeighborSummaries(
            neighborSummary,
            new GraphShardTopNSpec
            {
                Limit = 1,
                AggregateKey = "neighborAgeSum",
                Direction = GraphShardSortDirection.Desc
            });

        var ageHistogram = runtime.HistogramNodes(
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
        var ageBucketFilter = runtime.CreateNodeHistogramBucketFilter(
            "age",
            ageHistogram.Buckets[0],
            new GraphShardNodeFilterSpec
            {
                Predicate = new GraphShardNodePredicateSpec
                {
                    TableNames = ["Person"],
                    PropertyName = "age",
                    Operator = GraphShardPredicateOperator.Exists
                }
            });
        var filteredByBucket = runtime.FilterNodes(ageBucketFilter);

        var topNeighbor = Assert.Single(topNeighborSummary.Rows);
        Assert.Equal("KNOWS", topNeighbor.RelType);
        Assert.Equal("Person", topNeighbor.TargetTable);
        Assert.Equal(
            ["node:Person:bob", "node:Person:carol"],
            filteredByBucket.Select(match => match.Row.ExternalId).OrderBy(x => x));
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_BucketDrillDownHelpers_ProjectAndSummarizeInOneCall()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var aliceShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });
        var bobShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(GraphShardMerger.Merge(aliceShard, bobShard));

        var ageHistogram = runtime.HistogramNodes(
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
        var sinceHistogram = runtime.HistogramRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    RelTypes = ["KNOWS"],
                    PropertyName = "since",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            "since",
            1m);

        var nodeProjection = runtime.ProjectNodeHistogramBucketSubgraph(
            "age",
            ageHistogram.Buckets[0],
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
        var relationshipSummary = runtime.SummarizeRelationshipsForHistogramBucket(
            "since",
            sinceHistogram.Buckets[1],
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    RelTypes = ["KNOWS"],
                    PropertyName = "since",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            new GraphShardAggregateSpec { Key = "count", Function = GraphShardAggregateFunction.Count, Source = GraphShardAggregateSource.Relationship });

        Assert.Equal(2, nodeProjection.Stats.NodeCount);
        Assert.Equal(1, nodeProjection.Stats.EdgeCount);

        var relationshipRow = Assert.Single(relationshipSummary.Rows);
        Assert.Equal("KNOWS", relationshipRow.RelType);
        Assert.Equal(1, relationshipRow.Count);
        Assert.Equal(1, Convert.ToInt32(relationshipRow.AggregateValues["count"]));
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_ScopedFacets_AndShareMetadata_WorkAgainstMergedExtracts()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var aliceShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });
        var bobShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(GraphShardMerger.Merge(aliceShard, bobShard));

        var personAgeHistogram = runtime.HistogramNodesForTable("Person", "age", 5m);
        var knowsSummary = runtime.SummarizeRelationshipsForRelType(
            "KNOWS",
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    PropertyName = "since",
                    Operator = GraphShardPredicateOperator.Exists
                }
            });

        Assert.Equal(3, personAgeHistogram.TotalCount);
        Assert.Equal(2m / 3m, personAgeHistogram.Buckets[0].Share);

        var summaryRow = Assert.Single(knowsSummary.Rows);
        Assert.Equal(2, knowsSummary.TotalCount);
        Assert.Equal(1m, summaryRow.Share);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_RankedFacetHelpers_ComputeCumulativeShare()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var aliceShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });
        var bobShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(GraphShardMerger.Merge(aliceShard, bobShard));

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

        var ranked = runtime.RankNeighborSummaries(summary, new GraphShardTopNSpec { Limit = 2 });

        Assert.Equal(1, ranked.Rows[0].Rank);
        Assert.Equal(0.5m, ranked.Rows[0].CumulativeShare);
        Assert.Equal(2, ranked.Rows[1].Rank);
        Assert.Equal(1m, ranked.Rows[1].CumulativeShare);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_ComparisonHelpers_ComputeStableDeltas()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var aliceShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });
        var bobShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });

        var currentRuntime = GraphShardLocalRuntime.LoadExtract(GraphShardMerger.Merge(aliceShard, bobShard));
        var previousRuntime = GraphShardLocalRuntime.LoadExtract(aliceShard);

        var currentSummary = currentRuntime.SummarizeRelationshipsForRelType(
            "KNOWS",
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    PropertyName = "since",
                    Operator = GraphShardPredicateOperator.Exists
                }
            });
        var previousSummary = previousRuntime.SummarizeRelationshipsForRelType(
            "KNOWS",
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    PropertyName = "since",
                    Operator = GraphShardPredicateOperator.Exists
                }
            });

        var currentBuckets = currentRuntime.HistogramRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    RelTypes = ["KNOWS"],
                    PropertyName = "since",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            "since",
            1m);
        var previousBuckets = previousRuntime.HistogramRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    RelTypes = ["KNOWS"],
                    PropertyName = "since",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            "since",
            1m);

        var summaryDelta = currentRuntime.CompareRelationshipSummaries(currentSummary, previousSummary);
        var bucketDelta = currentRuntime.CompareHistogramBuckets(currentBuckets, previousBuckets);

        var summaryRow = Assert.Single(summaryDelta.Rows);
        Assert.Equal("Person -[KNOWS]-> Person", summaryRow.Label);
        Assert.Equal(1, summaryRow.CountDelta);

        var bucketRow = Assert.Single(bucketDelta.Rows, row => row.Key == "2021|2022");
        Assert.Equal("[2021, 2022)", bucketRow.Label);
        Assert.Equal(1, bucketRow.CountDelta);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_DeltaRankingAndProjectionDiff_HighlightBiggestMoversAcrossExtracts()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var currentShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });
        var previousShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        var currentRuntime = GraphShardLocalRuntime.LoadExtract(currentShard);
        var previousRuntime = GraphShardLocalRuntime.LoadExtract(previousShard);

        var currentSummary = currentRuntime.SummarizeRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    PropertyName = "since",
                    Operator = GraphShardPredicateOperator.Exists
                }
            });
        var previousSummary = previousRuntime.SummarizeRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    PropertyName = "since",
                    Operator = GraphShardPredicateOperator.Exists
                }
            });
        var currentBuckets = currentRuntime.HistogramRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    RelTypes = ["KNOWS"],
                    PropertyName = "since",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            "since",
            1m);
        var previousBuckets = previousRuntime.HistogramRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    RelTypes = ["KNOWS"],
                    PropertyName = "since",
                    Operator = GraphShardPredicateOperator.Exists
                }
            },
            "since",
            1m);

        var rankedSummaryDeltas = currentRuntime.RankSummaryDeltas(
            currentRuntime.CompareRelationshipSummaries(currentSummary, previousSummary),
            new GraphShardDeltaTopNSpec { Limit = 1, Metric = GraphShardDeltaMetric.CountDelta, Direction = GraphShardSortDirection.Desc });
        var rankedBucketDeltas = currentRuntime.RankBucketDeltas(
            currentRuntime.CompareHistogramBuckets(currentBuckets, previousBuckets),
            new GraphShardDeltaTopNSpec { Limit = 1, Metric = GraphShardDeltaMetric.CountDelta, Direction = GraphShardSortDirection.Desc });
        var projectionDelta = currentRuntime.CompareProjectedShards(previousShard);

        var summaryRow = Assert.Single(rankedSummaryDeltas.Rows);
        Assert.Equal("Person -[KNOWS]-> Person", summaryRow.Label);
        Assert.Equal(1, summaryRow.Rank);
        Assert.Equal(1, summaryRow.CountDelta);

        var bucketRow = Assert.Single(rankedBucketDeltas.Rows);
        Assert.Equal("[2021, 2022)", bucketRow.Label);
        Assert.Equal(1, bucketRow.Rank);
        Assert.Equal(1, bucketRow.CountDelta);

        Assert.Equal(["node:Person:carol"], projectionDelta.AddedNodeIds);
        Assert.Equal(["node:City:seattle"], projectionDelta.RemovedNodeIds);
        Assert.Single(projectionDelta.AddedRelIds);
        Assert.Single(projectionDelta.RemovedRelIds);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_GainersDeclinersAndProjectionSummaries_GroupExtractDiffs()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var currentShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });
        var previousShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        var currentRuntime = GraphShardLocalRuntime.LoadExtract(currentShard);
        var previousRuntime = GraphShardLocalRuntime.LoadExtract(previousShard);

        var currentSummary = currentRuntime.SummarizeRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    PropertyName = "since",
                    Operator = GraphShardPredicateOperator.Exists
                }
            });
        var previousSummary = previousRuntime.SummarizeRelationships(
            new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    PropertyName = "since",
                    Operator = GraphShardPredicateOperator.Exists
                }
            });
        var summaryDelta = currentRuntime.CompareRelationshipSummaries(currentSummary, previousSummary);
        var topGainers = currentRuntime.TopGainingSummaryDeltas(summaryDelta, new GraphShardDeltaTopNSpec { Limit = 1 });
        var topDecliners = currentRuntime.TopDecliningSummaryDeltas(
            new GraphShardSummaryDeltaResult
            {
                Rows =
                [
                    new GraphShardSummaryDeltaRow
                    {
                        Key = "LIVES_IN|Person|City",
                        Label = "Person -[LIVES_IN]-> City",
                        CountDelta = -1,
                        ShareDelta = -1m
                    }
                ]
            },
            new GraphShardDeltaTopNSpec { Limit = 1 });
        var nodeChangeSummary = currentRuntime.SummarizeProjectedNodeChanges(previousShard);
        var relationshipChangeSummary = currentRuntime.SummarizeProjectedRelationshipChanges(previousShard);

        var gainingRow = Assert.Single(topGainers.Rows);
        Assert.Equal("Person -[KNOWS]-> Person", gainingRow.Label);
        Assert.Equal(1, gainingRow.CountDelta);

        var decliningRow = Assert.Single(topDecliners.Rows);
        Assert.Equal("Person -[LIVES_IN]-> City", decliningRow.Label);
        Assert.Equal(-1, decliningRow.CountDelta);

        var personNodeDelta = Assert.Single(nodeChangeSummary.Rows, row => row.TableName == "Person");
        Assert.Equal(1, personNodeDelta.AddedCount);
        Assert.Equal(0, personNodeDelta.RemovedCount);

        var cityNodeDelta = Assert.Single(nodeChangeSummary.Rows, row => row.TableName == "City");
        Assert.Equal(0, cityNodeDelta.AddedCount);
        Assert.Equal(1, cityNodeDelta.RemovedCount);

        var knowsRelDelta = Assert.Single(relationshipChangeSummary.Rows, row => row.RelType == "KNOWS");
        Assert.Equal(1, knowsRelDelta.AddedCount);
        Assert.Equal(0, knowsRelDelta.RemovedCount);

        var livesInRelDelta = Assert.Single(relationshipChangeSummary.Rows, row => row.RelType == "LIVES_IN");
        Assert.Equal(0, livesInRelDelta.AddedCount);
        Assert.Equal(1, livesInRelDelta.RemovedCount);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_TopChangedAndChangeReview_ComposeStableExtractDiffView()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var currentShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });
        var previousShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(currentShard);

        var topNodeChanges = runtime.TopChangedNodeTables(previousShard, new GraphShardTopNSpec { Limit = 2 });
        var topRelationshipChanges = runtime.TopChangedRelationshipTypes(previousShard, new GraphShardTopNSpec { Limit = 2 });
        var review = runtime.BuildChangeReview(previousShard, new GraphShardTopNSpec { Limit = 1 });

        Assert.Equal(2, topNodeChanges.Rows.Count);
        Assert.Equal("City", topNodeChanges.Rows[0].TableName);
        Assert.Equal(-1, topNodeChanges.Rows[0].NetDelta);
        Assert.Equal(1, topNodeChanges.Rows[0].Rank);
        Assert.Equal("Person", topNodeChanges.Rows[1].TableName);
        Assert.Equal(1, topNodeChanges.Rows[1].NetDelta);

        Assert.Equal(2, topRelationshipChanges.Rows.Count);
        Assert.Equal("KNOWS", topRelationshipChanges.Rows[0].RelType);
        Assert.Equal(1, topRelationshipChanges.Rows[0].NetDelta);
        Assert.Equal("LIVES_IN", topRelationshipChanges.Rows[1].RelType);
        Assert.Equal(-1, topRelationshipChanges.Rows[1].NetDelta);

        Assert.Equal(["node:Person:carol"], review.ProjectionDelta.AddedNodeIds);
        Assert.Equal(["node:City:seattle"], review.ProjectionDelta.RemovedNodeIds);
        Assert.Single(review.TopChangedNodeTables.Rows);
        Assert.Single(review.TopChangedRelationshipTypes.Rows);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_SignedGroupedViewsAndHighlights_ExposeStableExtractReviewData()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var currentShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });
        var previousShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(currentShard);

        var topGainingNodes = runtime.TopGainingNodeTables(previousShard, new GraphShardTopNSpec { Limit = 2 });
        var topDecliningNodes = runtime.TopDecliningNodeTables(previousShard, new GraphShardTopNSpec { Limit = 2 });
        var topGainingRelationships = runtime.TopGainingRelationshipTypes(previousShard, new GraphShardTopNSpec { Limit = 2 });
        var topDecliningRelationships = runtime.TopDecliningRelationshipTypes(previousShard, new GraphShardTopNSpec { Limit = 2 });
        var overview = runtime.BuildChangeReviewOverview(previousShard);
        var highlights = runtime.BuildChangeReviewHighlights(previousShard, new GraphShardTopNSpec { Limit = 1 });

        var gainingNode = Assert.Single(topGainingNodes.Rows);
        Assert.Equal("Person", gainingNode.TableName);
        Assert.Equal(1, gainingNode.NetDelta);

        var decliningNode = Assert.Single(topDecliningNodes.Rows);
        Assert.Equal("City", decliningNode.TableName);
        Assert.Equal(-1, decliningNode.NetDelta);

        var gainingRelationship = Assert.Single(topGainingRelationships.Rows);
        Assert.Equal("KNOWS", gainingRelationship.RelType);
        Assert.Equal(1, gainingRelationship.NetDelta);

        var decliningRelationship = Assert.Single(topDecliningRelationships.Rows);
        Assert.Equal("LIVES_IN", decliningRelationship.RelType);
        Assert.Equal(-1, decliningRelationship.NetDelta);

        Assert.Equal(1, overview.AddedNodeCount);
        Assert.Equal(1, overview.RemovedNodeCount);
        Assert.Equal(1, overview.AddedRelCount);
        Assert.Equal(1, overview.RemovedRelCount);
        Assert.Equal(0, overview.NetNodeDelta);
        Assert.Equal(0, overview.NetRelDelta);
        Assert.Equal("nodes +1/-1, rels +1/-1", overview.SummaryLabel);

        Assert.Equal("nodes +1/-1, rels +1/-1", highlights.Overview.SummaryLabel);
        Assert.Single(highlights.TopGainingNodeTables.Rows);
        Assert.Single(highlights.TopDecliningNodeTables.Rows);
        Assert.Single(highlights.TopGainingRelationshipTypes.Rows);
        Assert.Single(highlights.TopDecliningRelationshipTypes.Rows);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_ChangeGroupSummaryAndDrilldown_ProjectStableExtractSubsets()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var currentShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });
        var previousShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(currentShard);

        var groupSummary = runtime.BuildChangeGroupSummary(previousShard);
        var personDrilldown = runtime.ProjectNodeTableChangeDrilldown(previousShard, "Person");
        var knowsDrilldown = runtime.ProjectRelationshipTypeChangeDrilldown(previousShard, "KNOWS");
        var reviewDrilldown = runtime.BuildChangeReviewDrilldown(previousShard, new GraphShardTopNSpec { Limit = 2 });

        Assert.Equal(2, groupSummary.ChangedNodeTableCount);
        Assert.Equal(1, groupSummary.GainingNodeTableCount);
        Assert.Equal(1, groupSummary.DecliningNodeTableCount);
        Assert.Equal(2, groupSummary.ChangedRelationshipTypeCount);
        Assert.Equal(1, groupSummary.GainingRelationshipTypeCount);
        Assert.Equal(1, groupSummary.DecliningRelationshipTypeCount);

        Assert.Equal(1, personDrilldown.AddedCount);
        Assert.Equal(0, personDrilldown.RemovedCount);
        Assert.Contains(personDrilldown.AddedProjection.NodeTables["Person"].Rows, row => row.ExternalId == "node:Person:carol");

        Assert.Equal(1, knowsDrilldown.AddedCount);
        Assert.Equal(0, knowsDrilldown.RemovedCount);
        Assert.Single(knowsDrilldown.AddedProjection.RelTables["KNOWS"].Rows);
        Assert.Equal("KNOWS", knowsDrilldown.AddedProjection.RelTables["KNOWS"].RelType);

        Assert.Equal(2, reviewDrilldown.NodeTableDrilldowns.Count);
        Assert.Equal(2, reviewDrilldown.RelationshipTypeDrilldowns.Count);
        Assert.Equal(2, reviewDrilldown.GroupSummary.ChangedRelationshipTypeCount);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_MultiGroupReview_ComposesSelectedExtractDrilldownsIntoStableMergedSubsets()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var currentShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });
        var previousShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(currentShard);

        var nodeComposition = runtime.ComposeNodeTableChangeDrilldowns(previousShard, ["City", "Person"]);
        var relationshipComposition = runtime.ComposeRelationshipTypeChangeDrilldowns(previousShard, ["KNOWS", "LIVES_IN"]);
        var selectedSummary = runtime.BuildSelectedChangeSummary(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"]);
        var multiReview = runtime.BuildMultiGroupChangeReview(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"], new GraphShardTopNSpec { Limit = 2 });

        Assert.Equal(2, nodeComposition.Summary.SelectedNodeTableCount);
        Assert.Equal(2, nodeComposition.Summary.AddedNodeCount);
        Assert.Equal(2, nodeComposition.Summary.RemovedNodeCount);
        Assert.Contains(nodeComposition.AddedProjection.NodeTables["Person"].Rows, row => row.ExternalId == "node:Person:carol");
        Assert.Contains(nodeComposition.RemovedProjection.NodeTables["City"].Rows, row => row.ExternalId == "node:City:seattle");

        Assert.Equal(2, relationshipComposition.Summary.SelectedRelationshipTypeCount);
        Assert.Equal(1, relationshipComposition.Summary.AddedRelCount);
        Assert.Equal(1, relationshipComposition.Summary.RemovedRelCount);
        Assert.Single(relationshipComposition.AddedProjection.RelTables["KNOWS"].Rows);
        Assert.Single(relationshipComposition.RemovedProjection.RelTables["LIVES_IN"].Rows);

        Assert.Equal(2, selectedSummary.SelectedNodeTableCount);
        Assert.Equal(2, selectedSummary.SelectedRelationshipTypeCount);
        Assert.Equal(2, selectedSummary.AddedNodeCount);
        Assert.Equal(2, selectedSummary.RemovedNodeCount);
        Assert.Equal(1, selectedSummary.AddedRelCount);
        Assert.Equal(1, selectedSummary.RemovedRelCount);

        Assert.Equal(2, multiReview.NodeTableComposition.Drilldowns.Count);
        Assert.Equal(2, multiReview.RelationshipTypeComposition.Drilldowns.Count);
        Assert.Equal("selected_groups", multiReview.CombinedComposition.Scope);
        Assert.Equal(2, multiReview.SelectionSummary.AddedNodeCount);
        Assert.Equal(2, multiReview.SelectionSummary.RemovedNodeCount);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_CompareMultiGroupReviews_ComputesStableExtractSelectionDeltas()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var currentShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });
        var previousShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(currentShard);

        var narrowReview = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 });
        var wideReview = runtime.BuildMultiGroupChangeReview(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"], new GraphShardTopNSpec { Limit = 2 });
        var comparison = runtime.CompareMultiGroupChangeReviews(wideReview, narrowReview);

        Assert.Equal(1, comparison.SelectionSummaryDelta.SelectedNodeTableCountDelta);
        Assert.Equal(1, comparison.SelectionSummaryDelta.SelectedRelationshipTypeCountDelta);
        Assert.Equal(0, comparison.SelectionSummaryDelta.AddedNodeCountDelta);
        Assert.Equal(2, comparison.SelectionSummaryDelta.RemovedNodeCountDelta);
        Assert.Equal(0, comparison.SelectionSummaryDelta.AddedRelCountDelta);
        Assert.Equal(1, comparison.SelectionSummaryDelta.RemovedRelCountDelta);

        Assert.Equal(["City", "Person"], comparison.NodeTableCompositionComparison.CurrentKeys);
        Assert.Equal(["Person"], comparison.NodeTableCompositionComparison.PreviousKeys);
        Assert.Contains("node:City:seattle", comparison.CombinedCompositionComparison.RemovedProjectionDelta.AddedNodeIds);
        Assert.Single(comparison.RelationshipTypeCompositionComparison.RemovedProjectionDelta.AddedRelIds);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_CompareMultiGroupReviewSeries_ComputesStableExtractBaselineSummary()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var currentShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });
        var previousShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(currentShard);

        var review0 = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 });
        var review1 = runtime.BuildMultiGroupChangeReview(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"], new GraphShardTopNSpec { Limit = 2 });
        var review2 = runtime.BuildMultiGroupChangeReview(previousShard, ["City"], ["LIVES_IN"], new GraphShardTopNSpec { Limit = 1 });
        var series = runtime.CompareMultiGroupChangeReviewSeries([review0, review1, review2], baselineIndex: 0);

        Assert.Equal(0, series.BaselineIndex);
        Assert.Equal(2, series.Rows.Count);
        Assert.Equal(1, series.Rows[0].Comparison.SelectionSummaryDelta.SelectedNodeTableCountDelta);
        Assert.Equal(1, series.Rows[0].Comparison.SelectionSummaryDelta.SelectedRelationshipTypeCountDelta);
        Assert.Equal(1, series.Summary.MaxRemovedRelCountDelta);
        Assert.Equal(2, series.Summary.MaxRemovedNodeCountDelta);
        Assert.Contains("baseline review[0]", series.Summary.SummaryLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_CompareNamedSeriesAndMatrix_UsesStableScopeKeys()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var currentShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });
        var previousShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(currentShard);
        var reviews = new Dictionary<string, GraphShardMultiGroupChangeReview>(StringComparer.Ordinal)
        {
            ["person-only"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 }),
            ["wide"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"], new GraphShardTopNSpec { Limit = 2 }),
            ["city-only"] = runtime.BuildMultiGroupChangeReview(previousShard, ["City"], ["LIVES_IN"], new GraphShardTopNSpec { Limit = 1 })
        };

        var namedSeries = runtime.CompareNamedMultiGroupChangeReviewSeries(reviews, "person-only");
        var matrix = runtime.CompareMultiGroupChangeReviewMatrix(reviews);

        Assert.Equal("person-only", namedSeries.BaselineKey);
        Assert.Equal(["city-only", "wide"], namedSeries.Rows.Select(row => row.Key).ToArray());
        Assert.Equal(2, namedSeries.Summary.ComparisonCount);
        Assert.Equal(["city-only", "person-only", "wide"], matrix.Keys);
        Assert.Equal(6, matrix.Summary.ComparisonCount);
        Assert.Contains(matrix.Cells, cell =>
            cell.CurrentKey == "wide" &&
            cell.PreviousKey == "person-only" &&
            cell.Comparison.SelectionSummaryDelta.SelectedNodeTableCountDelta == 1);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_BuildOverlapAndConsensus_ComputesCommonSelectedChanges()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var currentShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });
        var previousShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(currentShard);
        var reviews = new Dictionary<string, GraphShardMultiGroupChangeReview>(StringComparer.Ordinal)
        {
            ["person-only"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 }),
            ["wide"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"], new GraphShardTopNSpec { Limit = 2 })
        };

        var overlap = runtime.BuildMultiGroupChangeReviewOverlap(reviews);
        var consensus = runtime.BuildMultiGroupChangeReviewConsensus(reviews);

        Assert.Equal(["person-only", "wide"], overlap.Keys);
        Assert.Equal(["Person"], overlap.CommonNodeTableKeys);
        Assert.Equal(["KNOWS"], overlap.CommonRelationshipTypeKeys);
        Assert.Equal(["node:Person:bob", "node:Person:carol"], overlap.CombinedCompositionOverlap.CommonAddedNodeIds);
        Assert.Single(overlap.CombinedCompositionOverlap.CommonAddedRelIds);
        Assert.Equal(2, consensus.ScopeCount);
        Assert.Equal(1, consensus.CommonSummary.SelectedNodeTableCount);
        Assert.Equal(1, consensus.CommonSummary.SelectedRelationshipTypeCount);
        Assert.Equal(2, consensus.CommonSummary.AddedNodeCount);
        Assert.Contains("consensus across 2 scopes", consensus.CommonSummary.SummaryLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_BuildFrequencyAndThresholdConsensus_ComputesStableScopePresence()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var currentShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });
        var previousShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(currentShard);
        var reviews = new Dictionary<string, GraphShardMultiGroupChangeReview>(StringComparer.Ordinal)
        {
            ["person-only"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 }),
            ["wide"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"], new GraphShardTopNSpec { Limit = 2 }),
            ["city-only"] = runtime.BuildMultiGroupChangeReview(previousShard, ["City"], ["LIVES_IN"], new GraphShardTopNSpec { Limit = 1 })
        };

        var frequency = runtime.BuildMultiGroupChangeReviewFrequency(reviews);
        var threshold = runtime.BuildMultiGroupChangeReviewThresholdConsensus(reviews, 2);

        Assert.Equal(3, frequency.ScopeCount);
        Assert.Equal(["city-only", "person-only", "wide"], frequency.Keys);
        Assert.Equal(["City", "Person"], threshold.QualifiedNodeTableKeys);
        Assert.Equal(["KNOWS", "LIVES_IN"], threshold.QualifiedRelationshipTypeKeys);
        Assert.Equal(["node:Person:bob", "node:Person:carol"], threshold.ThresholdProjectionOverlap.CommonAddedNodeIds);
        Assert.Equal(2, threshold.ThresholdSummary.SelectedNodeTableCount);
        Assert.Equal(2, threshold.ThresholdSummary.SelectedRelationshipTypeCount);
        Assert.Contains(">= 2", threshold.ThresholdSummary.SummaryLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_BuildSelectionProfilesAndFamilies_ComputesStableScopeFamilies()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var currentShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });
        var previousShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(currentShard);
        var reviews = new Dictionary<string, GraphShardMultiGroupChangeReview>(StringComparer.Ordinal)
        {
            ["person-a"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 }),
            ["wide"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"], new GraphShardTopNSpec { Limit = 2 }),
            ["person-b"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 })
        };

        var profiles = runtime.BuildMultiGroupChangeReviewSelectionProfiles(reviews);
        var families = runtime.BuildMultiGroupChangeReviewSelectionFamilies(reviews);

        Assert.Equal(["person-a", "person-b", "wide"], profiles.Keys);
        Assert.Equal(3, profiles.Profiles.Count);
        Assert.Equal(2, families.Families.Count);
        Assert.Contains(families.Families, family =>
            family.Signature == "nodes[Person]|rels[KNOWS]" &&
            family.ScopeCount == 2);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_CompareSelectionProfilesAndFamilies_ComputesStableExtractShapeDeltas()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var currentShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });
        var previousShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(currentShard);
        var currentReviews = new Dictionary<string, GraphShardMultiGroupChangeReview>(StringComparer.Ordinal)
        {
            ["person-a"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 }),
            ["wide"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"], new GraphShardTopNSpec { Limit = 2 }),
            ["person-b"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 })
        };
        var previousReviews = new Dictionary<string, GraphShardMultiGroupChangeReview>(StringComparer.Ordinal)
        {
            ["person-a"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 }),
            ["city"] = runtime.BuildMultiGroupChangeReview(previousShard, ["City"], ["LIVES_IN"], new GraphShardTopNSpec { Limit = 1 }),
            ["person-b"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 })
        };

        var currentProfiles = runtime.BuildMultiGroupChangeReviewSelectionProfiles(currentReviews);
        var previousProfiles = runtime.BuildMultiGroupChangeReviewSelectionProfiles(previousReviews);
        var currentFamilies = runtime.BuildMultiGroupChangeReviewSelectionFamilies(currentReviews);
        var previousFamilies = runtime.BuildMultiGroupChangeReviewSelectionFamilies(previousReviews);

        var profileDelta = runtime.CompareSelectionProfiles(
            currentProfiles.Profiles.Single(profile => profile.Key == "wide"),
            previousProfiles.Profiles.Single(profile => profile.Key == "city"));
        var familyDelta = runtime.CompareSelectionFamilies(currentFamilies, previousFamilies);

        Assert.Equal(["Person"], profileDelta.AddedNodeTableKeys);
        Assert.Equal(["KNOWS"], profileDelta.AddedRelationshipTypeKeys);
        Assert.Contains(familyDelta.Rows, row =>
            row.Signature == "nodes[City,Person]|rels[KNOWS,LIVES_IN]" &&
            row.ScopeCountDelta == 1);
        Assert.Contains(familyDelta.Rows, row =>
            row.Signature == "nodes[City]|rels[LIVES_IN]" &&
            row.ScopeCountDelta == -1);
    }

    [Fact]
    public void ExtractMergeRuntimeFlow_BuildAndSummarizeSelectionSignatureTransitions_ComputesStableExtractTransitions()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var currentShard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });
        var previousShard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        var runtime = GraphShardLocalRuntime.LoadExtract(currentShard);
        var currentReviews = new Dictionary<string, GraphShardMultiGroupChangeReview>(StringComparer.Ordinal)
        {
            ["person"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 }),
            ["wide"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person", "City"], ["KNOWS", "LIVES_IN"], new GraphShardTopNSpec { Limit = 2 }),
            ["city"] = runtime.BuildMultiGroupChangeReview(previousShard, ["City"], ["LIVES_IN"], new GraphShardTopNSpec { Limit = 1 })
        };
        var previousReviews = new Dictionary<string, GraphShardMultiGroupChangeReview>(StringComparer.Ordinal)
        {
            ["person"] = runtime.BuildMultiGroupChangeReview(previousShard, ["Person"], ["KNOWS"], new GraphShardTopNSpec { Limit = 1 }),
            ["wide"] = runtime.BuildMultiGroupChangeReview(previousShard, ["City"], ["LIVES_IN"], new GraphShardTopNSpec { Limit = 1 })
        };

        var transitions = runtime.BuildSelectionSignatureTransitions(
            runtime.BuildMultiGroupChangeReviewSelectionProfiles(currentReviews),
            runtime.BuildMultiGroupChangeReviewSelectionProfiles(previousReviews));
        var summary = runtime.SummarizeSelectionSignatureTransitions(transitions);

        Assert.Equal(2, transitions.ChangedScopeCount);
        Assert.Equal(1, transitions.UnchangedScopeCount);
        Assert.Contains(summary.Rows, row =>
            row.PreviousSignature == "<missing>" &&
            row.CurrentSignature == "nodes[City]|rels[LIVES_IN]" &&
            row.ScopeCount == 1);
        Assert.Contains(summary.Rows, row =>
            row.PreviousSignature == "nodes[City]|rels[LIVES_IN]" &&
            row.CurrentSignature == "nodes[City,Person]|rels[KNOWS,LIVES_IN]" &&
            row.ScopeCount == 1);
    }
}
