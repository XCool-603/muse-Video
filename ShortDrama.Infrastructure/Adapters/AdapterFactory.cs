using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShortDrama.Application.Adapters;
using ShortDrama.Infrastructure.Data;

namespace ShortDrama.Infrastructure.Adapters
{
    /// <summary>
    /// 适配器工厂：负责注册、解析、启用/禁用各平台适配器。
    /// 启用状态来自 platform_sources 配置表，便于管理后台动态开关数据源。
    /// </summary>
    public class AdapterFactory : IAdapterFactory
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<AdapterFactory> _logger;
        private readonly ConcurrentDictionary<string, IPlatformAdapter> _adapters = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, bool> _enabled = new(StringComparer.OrdinalIgnoreCase);
        private bool _initialized;

        public AdapterFactory(IServiceProvider sp, ILogger<AdapterFactory> logger)
        {
            _sp = sp;
            _logger = logger;
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;

            foreach (var adapter in _sp.GetServices<IPlatformAdapter>())
            {
                _adapters[adapter.PlatformCode] = adapter;
                _enabled[adapter.PlatformCode] = true;
            }

            _initialized = true;
            _logger.LogInformation("已注册 {Count} 个平台适配器: {Codes}",
                _adapters.Count, string.Join(", ", _adapters.Keys));
        }

        public IReadOnlyList<IPlatformAdapter> GetAll()
        {
            EnsureInitialized();
            return _adapters.Values
                .Where(a => IsEnabled(a.PlatformCode))
                .ToList();
        }

        public IPlatformAdapter? Get(string platformCode)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(platformCode)) return null;
            return _adapters.TryGetValue(platformCode, out var adapter) ? adapter : null;
        }

        private bool IsEnabled(string code)
        {
            return !_enabled.TryGetValue(code, out var enabled) || enabled;
        }

        public async Task RefreshAsync()
        {
            EnsureInitialized();

            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sources = await db.PlatformSources.AsNoTracking().ToListAsync();

            if (sources.Count == 0) return;

            foreach (var source in sources)
            {
                _enabled[source.PlatformCode] = source.IsEnabled;
            }

            _logger.LogInformation("平台适配器启用状态已刷新，共 {Count} 条配置", sources.Count);
        }
    }
}
