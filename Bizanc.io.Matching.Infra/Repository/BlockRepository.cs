using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bizanc.io.Matching.Core.Domain;
using Bizanc.io.Matching.Core.Repository;
using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Operations.Indexes;
using Raven.Client.Documents.Session;
using Raven.Embedded;

namespace Bizanc.io.Matching.Infra.Repository
{
    public class BlockRepository : BaseRepository<Block>, IBlockRepository
    {
        public BlockRepository()
        : base()
        { }

        public async Task<bool> Contains(string hashStr)
        {
            using (var s = Store.OpenAsyncSession())
                return await s.Query<Block>().Where(b => b.HashStr == hashStr).AnyAsync();
        }

        public async Task SavePersistInfo(BlockPersistInfo info)
        {
            using (var s = Store.OpenAsyncSession())
            {
                await s.StoreAsync(info);
                await s.SaveChangesAsync();

                s.Advanced.WaitForIndexesAfterSaveChanges();
            }
        }

        public async Task DeletePersistInfo(string blockHash)
        {
            using (var s = Store.OpenAsyncSession())
            {
                string id = await s.Query<BlockPersistInfo>().Where(b => b.BlockHash == blockHash).Select(bl => bl.Id).FirstOrDefaultAsync();
                if (!string.IsNullOrEmpty(id))
                {
                    s.Delete(id);
                    await s.SaveChangesAsync();
                }
            }

            CleanIndexes();
        }

        private async void CleanIndexes()
        {
            IndexDefinition index = await Store.Maintenance.SendAsync(new GetIndexOperation("Auto/BlockPersistInfos/ByTimeStamp"));

            if (index != null)
                await Store.Maintenance.SendAsync(new DisableIndexOperation("Auto/BlockPersistInfos/ByTimeStamp"));

            index = await Store.Maintenance.SendAsync(new GetIndexOperation("Auto/BlockPersistInfos/ByBlockHashAndTimeStamp"));

            if (index != null)
                await Store.Maintenance.SendAsync(new DisableIndexOperation("Auto/BlockPersistInfos/ByBlockHashAndTimeStamp"));

            index = await Store.Maintenance.SendAsync(new GetIndexOperation("Auto/BlockPersistInfos/ByBlockHash"));

            if (index != null)
                await Store.Maintenance.SendAsync(new DisableIndexOperation("Auto/BlockPersistInfos/ByBlockHash"));
        }

        public async Task<Block> Get(string blockHash)
        {
            using (var s = Store.OpenAsyncSession())
                return await s.Query<Block>().Where(b => b.HashStr == blockHash).FirstOrDefaultAsync();
        }

        private async Task StreamResult(IAsyncDocumentSession session, IOrderedQueryable<Block> query, Channel<Block> result)
        {
            using (var stream = await session.Advanced.StreamAsync(query))
            {
                while (await stream.MoveNextAsync())
                    await result.Writer.WriteAsync(stream.Current.Document);
            }

            result.Writer.Complete();
        }

        public async Task<Channel<Block>> Get(long fromDepth, long toDepth = 0)
        {
            using (var s = Store.OpenAsyncSession())
            {
                var result = Channel.CreateUnbounded<Block>(); ;
                IOrderedQueryable<Block> query = null;
                if (toDepth == 0)
                    query = s.Query<Block>().Where(b => b.Header.Depth >= fromDepth).OrderBy(b => b.Header.Depth);
                else
                    query = s.Query<Block>().Where(b => b.Header.Depth >= fromDepth && b.Header.Depth <= toDepth).OrderBy(b => b.Header.Depth);

                await StreamResult(s, query, result);

                return result;
            }
        }

        public async Task<BlockPersistInfo> GetPersistInfo()
        {
            using (var s = Store.OpenAsyncSession())
                return await s.Query<BlockPersistInfo>().OrderByDescending(i => i.TimeStamp).FirstOrDefaultAsync();
        }

        public async Task<Stats> GetBlockStats()
        {
            var fromDate = DateTime.Now.ToUniversalTime().AddHours(-24);
            var result = new Stats();
            using (var s = Store.OpenAsyncSession())
            {
                var query = s.Query<Block>().Where(b => b.Header.TimeStamp >= fromDate);
                result.Blocks.TotalCount = await query.CountAsync();
                result.Blocks.FirstBlockTime = await query.Select(b => b.Header.TimeStamp).FirstOrDefaultAsync();
            }

            using (var s = Store.OpenAsyncSession())
            {
                var query = s.Query<Block>().Where(b => b.Header.TimeStamp >= fromDate);
                var agg = await query.Select(b => new
                {
                    d = b.Deposits.Count(),
                    o = b.Offers.Count(),
                    t = b.Transactions.Count(),
                    w = b.Withdrawals.Count()
                }).ToListAsync();

                foreach (var item in agg)
                {
                    result.Blocks.TotalOpCount += item.d + item.o + item.t + item.w;
                    result.Deposits.Total += item.d;
                    result.Offers.Total += item.o;
                    result.Transactions.Total += item.t;
                    result.Withdrawals.Total += item.w;
                }
            }

            return result;
        }
    }
}