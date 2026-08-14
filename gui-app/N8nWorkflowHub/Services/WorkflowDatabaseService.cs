using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using N8nWorkflowHub.Models;

namespace N8nWorkflowHub.Services
{
    public class WorkflowDatabaseService
    {
        private string _dbPath = string.Empty;
        private string _connectionString = string.Empty;

        public async Task InitializeAsync(IProgress<string>? progress = null)
        {
            await Task.Run(() =>
            {
                progress?.Report("Checking embedded workflow database...");

                string appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "N8nWorkflowHub"
                );
                Directory.CreateDirectory(appData);

                _dbPath = Path.Combine(appData, "workflows_v25.db");

                // Extract embedded resource if not existing
                var assembly = Assembly.GetExecutingAssembly();
                string resourceName = "N8nWorkflowHub.workflows_embedded.db";

                using var resourceStream = assembly.GetManifestResourceStream(resourceName);
                if (resourceStream == null)
                {
                    // Fallback to local file if running in dev environment
                    string localDevDb = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workflows_embedded.db");
                    if (File.Exists(localDevDb))
                    {
                        _dbPath = localDevDb;
                    }
                    else
                    {
                        throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
                    }
                }
                else
                {
                    // If file does not exist or size doesn't match resource size, extract it
                    if (!File.Exists(_dbPath) || new FileInfo(_dbPath).Length != resourceStream.Length)
                    {
                        progress?.Report("Extracting 22,500+ workflow templates into local high-speed cache...");
                        using var fileStream = File.Create(_dbPath);
                        resourceStream.CopyTo(fileStream);
                    }
                }

                _connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = _dbPath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Cache = SqliteCacheMode.Shared
                }.ToString();

                progress?.Report("Database initialized successfully.");
            });
        }

        public async Task<List<CategoryItem>> GetCategoriesAsync()
        {
            var list = new List<CategoryItem>();
            int total = 0;

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT category, COUNT(*) as cnt FROM workflows GROUP BY category ORDER BY category ASC;";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string cat = reader.GetString(0);
                int count = reader.GetInt32(1);
                total += count;
                list.Add(new CategoryItem { Name = cat, Count = count });
            }

            // Insert "All Categories" at top
            list.Insert(0, new CategoryItem { Name = "All Workflows", Count = total });
            return list;
        }

        public async Task<List<WorkflowItem>> SearchWorkflowsAsync(string query, string category, string nodeFilter, int limit = 500)
        {
            var results = new List<WorkflowItem>();

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            var conditions = new List<string>();

            if (!string.IsNullOrWhiteSpace(category) && category != "All Workflows")
            {
                conditions.Add("category = @category");
                cmd.Parameters.AddWithValue("@category", category);
            }

            if (!string.IsNullOrWhiteSpace(nodeFilter) && nodeFilter != "All Nodes")
            {
                conditions.Add("nodes_summary LIKE @nodeFilter");
                cmd.Parameters.AddWithValue("@nodeFilter", $"%{nodeFilter}%");
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                conditions.Add("(name LIKE @q OR tags LIKE @q OR nodes_summary LIKE @q OR id LIKE @q)");
                cmd.Parameters.AddWithValue("@q", $"%{query}%");
            }

            string whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
            cmd.CommandText = $"SELECT id, name, category, tags, nodes_summary FROM workflows {whereClause} ORDER BY name ASC LIMIT {limit};";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new WorkflowItem
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Category = reader.GetString(2),
                    Tags = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    NodesSummary = reader.IsDBNull(4) ? "" : reader.GetString(4)
                });
            }

            return results;
        }

        public async Task<string?> GetWorkflowJsonAsync(string id)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT json_gzip FROM workflows WHERE id = @id LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", id);

            var result = await cmd.ExecuteScalarAsync();
            if (result is byte[] compressedBytes)
            {
                using var msInput = new MemoryStream(compressedBytes);
                using var gzip = new GZipStream(msInput, CompressionMode.Decompress);
                using var msOutput = new MemoryStream();
                await gzip.CopyToAsync(msOutput);
                return Encoding.UTF8.GetString(msOutput.ToArray());
            }

            return null;
        }

        public async Task<List<(string name, string json)>> GetCategoryWorkflowsAsync(string category)
        {
            var results = new List<(string name, string json)>();

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            if (category == "All Workflows")
            {
                cmd.CommandText = "SELECT name, json_gzip FROM workflows;";
            }
            else
            {
                cmd.CommandText = "SELECT name, json_gzip FROM workflows WHERE category = @cat;";
                cmd.Parameters.AddWithValue("@cat", category);
            }

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string name = reader.GetString(0);
                byte[] compressedBytes = (byte[])reader[1];

                using var msInput = new MemoryStream(compressedBytes);
                using var gzip = new GZipStream(msInput, CompressionMode.Decompress);
                using var msOutput = new MemoryStream();
                await gzip.CopyToAsync(msOutput);
                string json = Encoding.UTF8.GetString(msOutput.ToArray());
                results.Add((name, json));
            }

            return results;
        }
    }
}
