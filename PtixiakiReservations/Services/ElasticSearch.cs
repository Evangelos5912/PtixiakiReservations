using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Elastic.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PtixiakiReservations.Configurations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PtixiakiReservations.Services
{
    // Concrete Elasticsearch implementation resolving indexing and cross-domain text querying.
    public class ElasticSearchService : IElasticSearch
    {
        private readonly ElasticsearchClient _client;
        private readonly ElasticSettings _settings;
        private readonly ILogger<ElasticSearchService> _logger;

        public ElasticSearchService(
            IOptions<ElasticSettings> options,
            ILogger<ElasticSearchService> logger)
        {
            _settings = options.Value;
            _logger = logger;

            try
            {
                var settings = new ElasticsearchClientSettings(new Uri(_settings.Uri))
                    .DefaultIndex(_settings.DefaultIndex)
                    .EnableDebugMode()
                    .ServerCertificateValidationCallback(CertificateValidations.AllowAll);

                _client = new ElasticsearchClient(settings);

                _logger.LogInformation("Elasticsearch client created with URL: {Url}", _settings.Uri);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Elasticsearch client");
                throw;
            }
        }

        // provisions specified index schema enforcing strict mapping logic.
        public async Task<bool> CreateIndexIfNotExistsAsync(string indexName)
        {
            try
            {
                var existsResponse = await _client.Indices.ExistsAsync(indexName);

                if (!existsResponse.Exists)
                {
                    var createResponse = await _client.Indices.CreateAsync(indexName, c => c
                        .Settings(s => s
                            .Analysis(a => a
                                .CharFilters(cf => cf
                                    .Mapping("greeklish", m => m
                                        .Mappings(new[] {
                                            "α=>a", "ά=>a", "β=>v", "γ=>g", "δ=>d", "ε=>e", "έ=>e", "ζ=>z", 
                                            "η=>i", "ή=>i", "θ=>th", "ι=>i", "ί=>i", "ϊ=>i", "ΐ=>i", "κ=>k", 
                                            "λ=>l", "μ=>m", "ν=>n", "ξ=>x", "ο=>o", "ό=>o", "π=>p", "ρ=>r", 
                                            "σ=>s", "ς=>s", "τ=>t", "υ=>y", "ύ=>y", "ϋ=>y", "ΰ=>y", "φ=>f", 
                                            "χ=>ch", "ψ=>ps", "ω=>o", "ώ=>o",
                                            "Α=>a", "Ά=>a", "Β=>v", "Γ=>g", "Δ=>d", "Ε=>e", "Έ=>e", "Ζ=>z", 
                                            "Η=>i", "Ή=>i", "Θ=>th", "Ι=>i", "Ί=>i", "Ϊ=>i", "Κ=>k", "Λ=>l", 
                                            "Μ=>m", "Ν=>n", "Ξ=>x", "Ο=>o", "Ό=>o", "Π=>p", "Ρ=>r", "Σ=>s", 
                                            "Τ=>t", "Υ=>y", "Ύ=>y", "Ϋ=>y", "Φ=>f", "Χ=>ch", "Ψ=>ps", "Ω=>o", "Ώ=>o"
                                        })
                                    )
                                )
                                .TokenFilters(tf => tf
                                    .Synonym("city_synonyms", syn => syn
                                        .Synonyms(new[] {
                                            "athina, athens",
                                            "thessaloniki, salonica, salonika",
                                            "patra, patras",
                                            "peiraias, piraeus, pireas"
                                        })
                                    )
                                )
                                .Analyzers(an => an
                                    .Custom("default", ca => ca
                                        .Tokenizer("standard")
                                        .CharFilter(new[] { "greeklish" })
                                        .Filter(new[] { "lowercase", "city_synonyms" })
                                    )
                                )
                            )
                        )
                    );

                    return createResponse.IsValidResponse;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking/creating index {IndexName}", indexName);
                return false;
            }
        }
        
        // Commits single document upsert into configured index collection.
        public async Task<bool> AddOrUpdateAsync<T>(T item, string indexName) where T : class
        {
            try
            {
                _logger.LogInformation("Indexing single document to {IndexName}", indexName);
                var response = await _client.IndexAsync(item, i => i.Index(indexName));

                if (!response.IsValidResponse)
                {
                    _logger.LogError("Failed to index document: {ErrorReason}",
                        response.ElasticsearchServerError?.Error?.Reason);
                }

                return response.IsValidResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing document to {IndexName}", indexName);
                return false;
            }
        }

        // Executes batched write operations for bulk entity synchronization.
        public async Task<bool> AddOrUpdateBulkAsync<T>(IEnumerable<T> items, string indexName) where T : class
        {
            try
            {
                var itemsList = items.ToList();
                _logger.LogInformation("Bulk indexing {Count} documents to {IndexName}",
                    itemsList.Count,
                    indexName);

                var bulkResponse = await _client.BulkAsync(b => b
                    .Index(indexName)
                    .IndexMany(itemsList));

                if (!bulkResponse.IsValidResponse)
                {
                    _logger.LogError("Bulk indexing failed: {ErrorReason}",
                        bulkResponse.ElasticsearchServerError?.Error?.Reason);

                    if (bulkResponse.Items != null)
                    {
                        foreach (var item in bulkResponse.Items.Where(i => !i.IsValid))
                        {
                            _logger.LogError("Item error: {ErrorReason}", item.Error?.Reason);
                        }
                    }
                }

                return bulkResponse.IsValidResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error bulk indexing to {IndexName}", indexName);
                return false;
            }
        }

        // Resolves singular indexed document via explicit identity.
        public async Task<T> GetByIdAsync<T>(string id, string indexName) where T : class
        {
            try
            {
                var response = await _client.GetAsync<T>(id, g => g.Index(indexName));
                return response.Source;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting document {Id} from {IndexName}", id, indexName);
                return null;
            }
        }

        // Translates string terms against indexed nodes applying fuzziness mapping logic.
        public async Task<IEnumerable<T>> SearchAsync<T>(string query, string indexName, double? lat = null, double? lon = null, string distance = "50km") where T : class
        {
            try
            {
                var response = await _client.SearchAsync<T>(s => s
                    .Index(indexName)
                    .Query(q => q
                        .Bool(b => 
                        {
                            b.Must(mu => mu
                                .MultiMatch(m => m
                                    .Fields("*")
                                    .Query(query)
                                    .Fuzziness(new Fuzziness("AUTO")) 
                                )
                            );

                            if (lat.HasValue && lon.HasValue && !string.IsNullOrEmpty(distance))
                            {
                                b.Filter(fi => fi
                                    .GeoDistance(geo => geo
                                        .Field("location")
                                        .Distance(distance)
                                        .Location(new LatLonGeoLocation { Lat = lat.Value, Lon = lon.Value })
                                    )
                                );
                            }
                        })
                    )
                );

                return response.IsValidResponse ? response.Documents : Enumerable.Empty<T>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching for '{Query}' in {IndexName}", query, indexName);
                return Enumerable.Empty<T>();
            }
        }

        // Flushes entity metadata spanning targeted identity constraints.
        public async Task<bool> DeleteByIdAsync<T>(string id, string indexName) where T : class
        {
            try
            {
                var deleteResponse = await _client.DeleteAsync<T>(id, d => d.Index(indexName));
                return deleteResponse.IsValidResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting document {Id} from {IndexName}", id, indexName);
                return false;
            }
        }
    }
}