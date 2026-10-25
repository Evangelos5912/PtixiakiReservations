using PtixiakiReservations.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PtixiakiReservations.Services
{
    public interface IElasticSearch
    {
        Task<bool> CreateIndexIfNotExistsAsync(string indexName);

        Task<bool> AddOrUpdateAsync<T>(T item, string indexName) where T : class;

        Task<bool> AddOrUpdateBulkAsync<T>(IEnumerable<T> items, string indexName) where T : class;

        Task<T> GetByIdAsync<T>(string id, string indexName) where T : class;

        Task<IEnumerable<T>> SearchAsync<T>(string query, string indexName, double? lat = null, double? lon = null, string distance = "50km") where T : class;
        Task<bool> DeleteByIdAsync<T>(string id, string indexName) where T : class;
    }
}