﻿using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using LinkDotNet.Blog.Domain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using X.PagedList;

namespace LinkDotNet.Blog.Infrastructure.Persistence
{
    public class CachedRepository<T> : IRepository<T>
        where T : Entity
    {
        private static CancellationTokenSource resetToken = new();

        private readonly IRepository<T> repository;

        private readonly IMemoryCache memoryCache;

        public CachedRepository(IRepository<T> repository, IMemoryCache memoryCache)
        {
            this.repository = repository;
            this.memoryCache = memoryCache;
        }

        private static MemoryCacheEntryOptions Options => new()
        {
            ExpirationTokens = { new CancellationChangeToken(resetToken.Token) },
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7),
        };

        public async Task<T> GetByIdAsync(string id)
        {
            if (!memoryCache.TryGetValue(id, out T value))
            {
                value = await repository.GetByIdAsync(id);
                memoryCache.Set(id, value, Options);
            }

            return value;
        }

        public async Task<IPagedList<T>> GetAllAsync(
            Expression<Func<T, bool>> filter = null,
            Expression<Func<T, object>> orderBy = null,
            bool descending = true,
            int page = 1,
            int pageSize = int.MaxValue)
        {
            var key = $"{filter?.Body}-{orderBy?.Body}-{descending}-{page}-{pageSize}";
            return await memoryCache.GetOrCreate(key, async e =>
            {
                e.SetOptions(Options);
                return await repository.GetAllAsync(filter, orderBy, descending, page, pageSize);
            });
        }

        public async Task StoreAsync(T entity)
        {
            await repository.StoreAsync(entity);
            ResetCache();
            memoryCache.Set(entity.Id, entity, Options);
        }

        public async Task DeleteAsync(string id)
        {
            ResetCache();
            memoryCache.Remove(id);
            await repository.DeleteAsync(id);
        }

        private static void ResetCache()
        {
            if (resetToken is { IsCancellationRequested: false, Token: { CanBeCanceled: true } })
            {
                resetToken.Cancel();
                resetToken.Dispose();
            }

            resetToken = new CancellationTokenSource();
        }
    }
}