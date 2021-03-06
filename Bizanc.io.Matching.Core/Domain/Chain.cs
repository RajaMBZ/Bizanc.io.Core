using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using Bizanc.io.Matching.Core.Crypto;
using System.Collections.Immutable;
using SimpleBase;
using Bizanc.io.Matching.Core.Domain.Immutable;
using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace Bizanc.io.Matching.Core.Domain
{
    public class Chain
    {
        public TransactionManager TransactManager { get; private set; } = new TransactionManager();
        public Immutable.Deposit DepositManager { get; private set; } = new Immutable.Deposit();
        public Immutable.Withdrawal WithdrawalManager { get; private set; } = new Immutable.Withdrawal();
        public Immutable.Book BookManager { get; private set; } = new Immutable.Book();
        public Pool Pool { get; private set; } = new Pool();
        public Guid Id { get; private set; } = Guid.NewGuid();
        public Block LastBlock { get; private set; }
        public Block CurrentBlock { get; private set; }
        public bool Mining { get; private set; } = false;
        public bool Mined { get; set; } = false;
        public CancellationTokenSource CancelToken { get; set; } = new CancellationTokenSource();
        public bool Initialized { get; set; } = false;

        public long Count { get { return CurrentBlock != null ? CurrentBlock.Header.Depth + 1 : 0; } }

        private string minerWallet;
        public Chain Previous { get; private set; }
        public DateTime Timestamp { get; private set; } = DateTime.Now;
        private SemaphoreSlim commitLocker = new SemaphoreSlim(1, 1);

        public Chain()
        {

        }

        public Chain(Chain previous)
        {
            if (previous != null)
            {
                Previous = previous;
                this.BookManager = previous.BookManager;
                this.CancelToken = new CancellationTokenSource();
                this.DepositManager = previous.DepositManager;
                this.Id = Guid.NewGuid();
                this.minerWallet = previous.minerWallet;
                this.Mining = false;
                this.TransactManager = previous.TransactManager;
                this.WithdrawalManager = previous.WithdrawalManager;
                this.Pool = previous.Pool;
            }
        }

        public Chain(Chain previous, Block genesis, Pool pool)
            : this(previous)
        {
            this.CurrentBlock = genesis;
            this.LastBlock = genesis;
            this.Pool = pool;
        }

        public Chain(Chain previous,
                TransactionManager transact,
                Immutable.Deposit deposit,
                Immutable.Withdrawal withdrawal,
                Immutable.Book book,
                Block currentBlock,
                Block lastBlock,
                Pool pool)
                : this(previous)
        {
            this.BookManager = book;
            this.CurrentBlock = currentBlock;
            this.DepositManager = deposit;
            this.LastBlock = lastBlock;
            this.TransactManager = transact;
            this.WithdrawalManager = withdrawal;
            this.Pool = pool;
        }

        public async Task<ICollection<Transaction>> GetTransactionPool() => await Pool.TransactionPool.GetPool();

        public async Task<ICollection<Deposit>> GetDepositPool() => await Pool.DepositPool.GetPool();

        public async Task<ICollection<Withdrawal>> GetWithdrawalPool() => await Pool.WithdrawalPool.GetPool();

        public async Task<ICollection<Offer>> GetOfferPool() => await Pool.OfferPool.GetPool();

        public async Task<ICollection<OfferCancel>> GetOfferCancelPool() => await Pool.OfferCancelPool.GetPool();


        public List<Block> GetBlocksNewToOld(List<Block> result = null)
        {
            if (result == null)
                result = new List<Block>();

            if (CurrentBlock != null)
                result.Add(CurrentBlock);

            if (Previous != null)
                result = Previous.GetBlocksNewToOld(result);

            return result;
        }

        public List<Block> GetBlocksOldToNew(List<Block> result = null)
        {
            if (result == null)
                result = new List<Block>();

            if (Previous != null)
                result = Previous.GetBlocksOldToNew(result);

            if (CurrentBlock != null)
                result.Add(CurrentBlock);

            return result;
        }

        public async Task<List<Transaction>> GetAllTransactions()
        {
            var result = new List<Transaction>();
            result.AddRange(await Pool.TransactionPool.GetPool());
            GetBlocksNewToOld().ForEach(b => result.AddRange(b.Transactions));
            return result;
        }

        public async Task<List<Deposit>> GetAllDeposits()
        {
            var result = new List<Deposit>();
            result.AddRange(await Pool.DepositPool.GetPool());
            GetBlocksNewToOld().ForEach(b => result.AddRange(b.Deposits));
            return result;
        }

        public async Task<List<Withdrawal>> GetAllWithdrawals()
        {
            var result = new List<Withdrawal>();
            result.AddRange(await Pool.WithdrawalPool.GetPool());
            GetBlocksNewToOld().ForEach(b => result.AddRange(b.Withdrawals));
            return result;
        }

        public async Task<List<Offer>> GetAllOffers()
        {
            var result = new List<Offer>();
            result.AddRange(await Pool.OfferPool.GetPool());
            GetBlocksNewToOld().ForEach(b => result.AddRange(b.Offers));

            return result;
        }

        public DateTime GetLastBlockTime()
        {
            if (Previous != null)
            {
                var result = Previous.GetLastBlockTime();
                if (result != default(DateTime))
                    return result;
            }

            if (CurrentBlock != null)
                return CurrentBlock.Timestamp;

            return default(DateTime);
        }

        public DateTime GetLastBlockTime(int limit, int count = 0)
        {
            if (Previous != null)
            {
                if (count < limit)
                {
                    var result = Previous.GetLastBlockTime(limit, ++count);
                    if (result != default(DateTime))
                        return result;
                }
            }

            if (CurrentBlock != null)
                return CurrentBlock.Timestamp;

            return default(DateTime);
        }

        public long GetLastBlockTimeTicks()
        {
            if (Previous != null)
            {
                var result = Previous.GetLastBlockTimeTicks();
                if (result != default(DateTime).Ticks)
                    return result;
            }

            if (CurrentBlock != null)
                return CurrentBlock.Timestamp.Ticks;

            return default(DateTime).Ticks;
        }

        public long GetLastBlockDepth()
        {
            if (Previous != null)
            {
                var result = Previous.GetLastBlockDepth();
                if (result != -1)
                    return result;
            }

            if (CurrentBlock != null)
                return CurrentBlock.Header.Depth;

            return -1;
        }

        public List<Offer> GetProcessedOffers(string wallet, int count = 0)
        {
            var result = new List<Offer>();

            if (count < 17 && Previous != null)
                result.AddRange(Previous.GetProcessedOffers(wallet, ++count));

            if (CurrentBlock != null)
                result.AddRange(BookManager.GetProcessedOffers(wallet));

            return result;
        }

        public List<Trade> GetTrades(string asset, int count = 0)
        {
            var result = new List<Trade>();

            if (count < 17 && Previous != null)
                result.AddRange(Previous.GetTrades(asset, ++count));

            result.AddRange(BookManager.GetTrades(asset));

            return result;
        }

        public OfferBook GetBook(string asset)
        {
            return BookManager.GetBook(asset);
        }
        public async Task Initialize(string minerWallet)
        {
            this.minerWallet = minerWallet;
            Initialized = true;

            await Task.CompletedTask;
        }

        private async Task<Block> Genesis()
        {
            Console.WriteLine("Starting Genesis");
            var genesis = new Block();

            genesis.Header.Difficult = 21;

            genesis.Header.TimeStamp = DateTime.Now;
            genesis.BuildMerkleRoot();
            CancelToken = new CancellationTokenSource();
            return await Mine(genesis, CancelToken);
        }

        public async Task UpdatePool()
        {
            await Pool.Remove(CurrentBlock.Deposits);
            await Pool.Remove(CurrentBlock.Offers);
            await Pool.Remove(CurrentBlock.OfferCancels);
            var tx = CurrentBlock.Transactions.ToList();
            if (tx.Count > 0)
                tx.RemoveAt(0);

            await Pool.Remove(tx);
            await Pool.Remove(CurrentBlock.Withdrawals);
        }

        public async Task EnterCommitLock()
        {
            await commitLocker.WaitAsync();
        }

        public async Task<bool> Contains(Transaction tx, bool first = true)
        {
            if (first && await Pool.Contains(tx))
                return true;

            if (CurrentBlock != null)
            {
                if (CurrentBlock.TransactionsDictionary.ContainsKey(tx.HashStr))
                    return true;

                if (Previous != null)
                    return await Previous.Contains(tx, false);
            }

            return false;
        }

        public async Task<bool> Append(Transaction tx)
        {
            if (!await Contains(tx))
                return await Pool.Add(tx);

            return false;
        }

        public async Task<bool> Append(Offer of)
        {
            if (!await Contains(of))
                return await Pool.Add(of);

            return false;
        }

        public async Task<bool> Append(OfferCancel of)
        {
            if (!await Contains(of))
                return await Pool.Add(of);

            return false;
        }

        public async Task<bool> Append(Deposit dp)
        {
            if (!await Contains(dp))
                return await Pool.Add(dp);

            return false;
        }

        public async Task<bool> Append(Withdrawal wd)
        {
            if (!await Contains(wd))
                return await Pool.Add(wd);

            return false;
        }

        public bool Contains(Block block)
        {
            if (CurrentBlock != null)
            {
                if (CurrentBlock.HashStr == block.HashStr)
                    return true;

                if (Previous != null)
                    return Previous.Contains(block);
            }

            return false;
        }

        public Deposit GetDeposit(string hash)
        {
            if (CurrentBlock != null)
            {
                if (CurrentBlock.DepositsDictionary.ContainsKey(hash))
                    return CurrentBlock.DepositsDictionary[hash];

                if (Previous != null)
                    return Previous.GetDeposit(hash);
            }

            return null;
        }

        public Offer GetProcessedOffer(string hash)
        {
            Offer result = null;

            if (CurrentBlock != null)
            {
                result = BookManager.GetProcessedOffer(hash);

                if (result == null && Previous != null)
                    return Previous.GetProcessedOffer(hash);
            }

            return result;
        }

        public Offer GetMinedOffer(string hash)
        {
            Offer result = null;

            if (CurrentBlock != null)
            {
                if (CurrentBlock.OffersDictionary.ContainsKey(hash))
                    return CurrentBlock.OffersDictionary[hash];

                if (result == null && Previous != null)
                    return Previous.GetMinedOffer(hash);
            }

            return result;
        }

        public Transaction GetTransaction(string hash)
        {
            if (CurrentBlock != null)
            {
                if (CurrentBlock.TransactionsDictionary.ContainsKey(hash))
                    return CurrentBlock.TransactionsDictionary[hash];

                if (Previous != null)
                    return Previous.GetTransaction(hash);
            }

            return null;
        }

        public Withdrawal GetWithdrawal(string hash)
        {
            if (CurrentBlock != null)
            {
                if (CurrentBlock.WithdrawalsDictionary.ContainsKey(hash))
                    return CurrentBlock.WithdrawalsDictionary[hash];

                if (Previous != null)
                    return Previous.GetWithdrawal(hash);
            }

            return null;
        }

        public async Task<bool> Contains(Deposit dp, bool first = true)
        {
            if (first && await Pool.Contains(dp))
                return true;

            if (CurrentBlock != null)
            {
                if (CurrentBlock.DepositsDictionary.ContainsKey(dp.HashStr))
                    return true;

                if (Previous != null)
                    return await Previous.Contains(dp, false);
            }

            return false;
        }

        public async Task<bool> Contains(Offer of, bool first = true)
        {
            if (first && (await Pool.Contains(of) || BookManager.ContainsOffer(of)))
                return true;

            if (CurrentBlock != null)
            {
                if (CurrentBlock.OffersDictionary.ContainsKey(of.HashStr))
                    return true;

                if (Previous != null)
                    return await Previous.Contains(of, false);
            }

            return false;
        }

        public async Task<bool> Contains(OfferCancel of, bool first = true)
        {
            if (first && await Pool.Contains(of))
                return true;

            if (CurrentBlock != null)
            {
                if (CurrentBlock.OffersCancelDictionary.ContainsKey(of.HashStr))
                    return true;

                if (Previous != null)
                    return await Previous.Contains(of, false);
            }

            return false;
        }


        public async Task<bool> Contains(Withdrawal wd, bool first = true)
        {
            if (first && await Pool.Contains(wd))
                return true;

            if (CurrentBlock != null)
            {
                if (CurrentBlock.WithdrawalsDictionary.ContainsKey(wd.HashStr))
                    return true;

                if (Previous != null)
                    return await Previous.Contains(wd, false);
            }

            return false;
        }

        public void ExitCommitLock()
        {
            commitLocker.Release();
        }

        public async Task<Chain> StartMining()
        {
            try
            {
                await commitLocker.WaitAsync();
                if (!CancelToken.IsCancellationRequested && !Mining)
                    Mining = true;
                else
                {
                    Console.WriteLine("Already Mining...");
                    return null;
                }
            }
            finally
            {
                commitLocker.Release();
            }

            Mining = true;

            if (CurrentBlock == null)
            {
                var genesis = await Genesis();
                if (genesis != null)
                    return new Chain(this, genesis, Pool);
            }
            else
            {
                var block = new Block();
                block.Header.Depth = CurrentBlock.Header.Depth + 1;
                block.Header.PreviousBlockHash = CurrentBlock.Header.Hash;
                block.Header.Difficult = GetTargetDiff();

                byte[] root = new byte[0];

                var deposit = new Immutable.Deposit(DepositManager, TransactManager);
                List<Deposit> ellegibleDeposits = null;
                (deposit, ellegibleDeposits, root) = deposit.ProcessDeposits(root, await GetDepositPool());
                var transact = deposit.TransactManager;
                block.Deposits = ellegibleDeposits;

                var book = new Book(BookManager, transact);
                var ellegibleOffers = new List<Offer>();
                (book, ellegibleOffers, root) = book.ProcessOffers(root, await GetOfferPool());
                transact = book.TransactManager;
                block.Offers = ellegibleOffers;

                var ellegibleOfferCancels = new List<OfferCancel>();
                (book, ellegibleOfferCancels, root) = book.ProcessOfferCancels(root, await GetOfferCancelPool());
                transact = book.TransactManager;
                block.OfferCancels = ellegibleOfferCancels;

                List<Transaction> ellegibleTransactions = null;
                (transact, ellegibleTransactions, root) = transact.ProcessTransactions(root, minerWallet, await GetTransactionPool(), CancelToken);
                block.Transactions = ellegibleTransactions;

                if (CancelToken.IsCancellationRequested)
                    return null;

                var withdrawal = new Immutable.Withdrawal(WithdrawalManager, transact);
                List<Withdrawal> ellegibleWithdrawals = null;
                (withdrawal, ellegibleWithdrawals, root) = withdrawal.ProcessWithdrawals(root, await GetWithdrawalPool());
                transact = withdrawal.TransactManager;
                block.Withdrawals = ellegibleWithdrawals;

                block.Offers = ellegibleOffers;
                Console.WriteLine("Mining New Block");
                Console.WriteLine(ellegibleTransactions.Count + " transactions");
                Console.WriteLine(ellegibleDeposits.Count + " deposits");
                Console.WriteLine(ellegibleWithdrawals.Count + " withdrawals");
                Console.WriteLine(ellegibleOffers.Count + " offers");

                block.Header.MerkleRoot = root;
                block.Header.TimeStamp = DateTime.Now;
                var result = await Mine(block, CancelToken);

                if (result != null)
                {
                    Console.WriteLine("Øting commit lock");
                    Console.WriteLine("Got commit lock");
                    try
                    {
                        await commitLocker.WaitAsync();
                        if (!CancelToken.IsCancellationRequested)
                        {
                            CancelToken.Cancel();
                            Mining = false;
                            Mined = true;
                            transact.Balance.BlockHash = result.HashStr;
                            book.BlockHash = result.HashStr;
                            return new Chain(this, transact, deposit, withdrawal, book, result, CurrentBlock, Pool);
                        }
                    }
                    finally
                    {
                        commitLocker.Release();
                    }
                }
            }

            return await Task<Chain>.FromResult((Chain)null);
        }

        private async Task<Block> Mine(Block block, CancellationTokenSource cancel)
        {
            Mining = true;

            try
            {
                Console.WriteLine("Mining Block, DIfculty: " + block.Header.Difficult);

                BlockHeader header = block.Header;
                var sw = new Stopwatch();
                sw.Start();

                var hash = CryptoHelper.Hash(header.ToString());

                await Task.Run(delegate
                {
                    while (!CryptoHelper.IsValidHash(header.Difficult, hash) && !cancel.IsCancellationRequested)
                    {
                        header.Nonce++;
                        hash = CryptoHelper.Hash(header.ToString());
                    }
                });



                if (!cancel.IsCancellationRequested)
                {
                    sw.Stop();

                    Console.WriteLine("Found hash: " + Base58.Bitcoin.Encode(new Span<Byte>(hash)));
                    Console.WriteLine("Nonce: " + header.Nonce);
                    Console.WriteLine("Total Time: " + sw.Elapsed);

                    block.Header.Hash = hash;
                    block.Header.Status = BlockStatus.Mined;
                    return block;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            Mining = false;
            return null;
        }

        public void StopMining()
        {
            CancelToken.Cancel();
        }

        private bool IsValidDificulty(byte[] lastDiff, byte[] currentDiff)
        {
            var result = true;

            for (int i = 0; i < lastDiff.Length; i++)
            {
                result = result && (lastDiff[i] == currentDiff[i]);
            }

            return result;
        }
        static int slCount = 0;
        private async Task<(bool, TransactionManager, byte[])> ValidateTransactions(byte[] root, Block block, TransactionManager transact)
        {
            if (block.TransactionsDictionary.Count > 0)
            {
                Console.WriteLine("Validating transactions...");
                //Thread.Sleep(300 * slCount);
                slCount++;
                var foundMineTransaction = false;

                Transaction miningTransaction = null;

                foreach (var tx in block.Transactions)
                {
                    if (CancelToken.IsCancellationRequested)
                        return (false, null, null);

                    root = CryptoHelper.Hash(Base58.Bitcoin.Encode(new Span<Byte>(root)) + tx.ToString());

                    if (!foundMineTransaction
                        && String.IsNullOrEmpty(tx.Wallet)
                        && String.IsNullOrEmpty(tx.Signature)
                        && tx.Outputs.Count == 1
                        && tx.Outputs[0].Size == 100)
                    {
                        foundMineTransaction = true;
                        miningTransaction = tx;
                        tx.Finish();
                        transact = transact.ProcessTransaction(tx);
                    }
                    else
                    {
                        if (!await Pool.Contains(tx))
                        {

                            Console.WriteLine("Block with invalid transaction.");
                            Console.WriteLine(JsonConvert.SerializeObject(tx));
                            Pool.TransactionPool.VerifyTX(tx);

                            var bk = CurrentBlock;
                            var prev = this;

                            while (bk != null && !bk.Transactions.Any(t => t.HashStr == tx.HashStr))
                            {
                                Console.WriteLine("NOT FOUND ON BLOCK " + bk.Header.Depth);
                                prev = prev.Previous;
                                if (prev != null)
                                    bk = prev.CurrentBlock;
                                else
                                    bk = null;
                            }

                            if (bk == null)
                                Console.WriteLine("Not found on chain");
                            else
                                Console.WriteLine("Found on chain");
                            return (false, null, null);
                        }


                        if (transact.CanProcess(tx))
                        {
                            transact = transact.ProcessTransaction(tx);
                        }
                        else
                        {
                            Console.WriteLine("Block with Transaction without balance");
                            Console.WriteLine("HasBalance " + transact.HasBalance(tx.Wallet, tx.Asset, tx.Outputs.Sum(o => o.Size)));
                            Console.WriteLine("Wallet " + tx.Wallet);
                            Console.WriteLine("Output " + tx.Outputs.Sum(o => o.Size));
                            return (false, null, null);
                        }
                    }
                }
            }

            Console.WriteLine("Transactions validated");
            return (true, transact, root);
        }

        private int GetTargetDiff()
        {
            var lastDeth = this.GetLastBlockDepth();
            var targetDiff = CurrentBlock.Header.Difficult;
            if (CurrentBlock != null && (CurrentBlock.Header.Depth - 20) >= lastDeth)
            {
                var frame = (CurrentBlock.Timestamp - this.GetLastBlockTime(20));
                var avg = frame.TotalSeconds / 20;

                if (targetDiff > 15 && avg > 40
                    && CurrentBlock.Header.Difficult == Previous.CurrentBlock.Header.Difficult
                    && CurrentBlock.Header.Difficult == Previous.Previous.CurrentBlock.Header.Difficult
                    && CurrentBlock.Header.Difficult == Previous.Previous.Previous.CurrentBlock.Header.Difficult
                    && CurrentBlock.Header.Difficult == Previous.Previous.Previous.Previous.CurrentBlock.Header.Difficult
                    && CurrentBlock.Header.Difficult == Previous.Previous.Previous.Previous.Previous.CurrentBlock.Header.Difficult)
                {
                    targetDiff -= 1;
                }

                if (avg < 20
                    && CurrentBlock.Header.Difficult == Previous.CurrentBlock.Header.Difficult
                    && CurrentBlock.Header.Difficult == Previous.Previous.CurrentBlock.Header.Difficult
                    && CurrentBlock.Header.Difficult == Previous.Previous.Previous.CurrentBlock.Header.Difficult
                    && CurrentBlock.Header.Difficult == Previous.Previous.Previous.Previous.CurrentBlock.Header.Difficult
                    && CurrentBlock.Header.Difficult == Previous.Previous.Previous.Previous.Previous.CurrentBlock.Header.Difficult)
                {
                    targetDiff += 1;
                }
            }

            return targetDiff;
        }

        private async Task<Chain> ProcessBlock(Block block)
        {
            Console.WriteLine("Starting process block");
            if (Previous == null && CurrentBlock == null && block.Header.PreviousBlockHash == null && block.TransactionsDictionary.Count == 0)
            {
                Console.WriteLine("Creating genesis chain");
                CancelToken.Cancel();
                Mining = false;
                Console.WriteLine("Genesis chain pool created");
                return new Chain(this, block, Pool);
            }

            if (block.Timestamp <= CurrentBlock.Timestamp || block.Timestamp > DateTime.Now.ToUniversalTime())
            {
                Console.WriteLine("Block with invalid timestamp");
                return null;
            }

            Console.WriteLine("Ceirifying dificulty");

            var lastDeth = this.GetLastBlockDepth();
            if (CurrentBlock != null && (CurrentBlock.Header.Depth - 20) >= lastDeth && block.Header.Difficult != GetTargetDiff())
            {
                Console.WriteLine("Invalid Block Difficulty");
                return null;
            }

            Console.WriteLine("Verifying Depth");

            if (CurrentBlock != null && block.Header.Depth != CurrentBlock.Header.Depth + 1)
            {
                Console.WriteLine("Invalid Block Depth");
                Console.WriteLine("Current Block Depth " + CurrentBlock.Header.Depth);
                Console.WriteLine("Current Block Hash " + CurrentBlock.HashStr);
                Console.WriteLine("Received Block Depth " + block.Header.Depth);
                Console.WriteLine("Received Block Previous Hash " + block.PreviousHashStr);
                return null;
            }

            Console.WriteLine("Verifying previous hash null");
            if (block.Header.PreviousBlockHash == null)
            {
                Console.WriteLine("Received Empty previous block hash after genesis");
                return null;
            }

            Console.WriteLine("Verifying previous block hash match");
            if (CurrentBlock != null && !block.Header.PreviousBlockHash.SequenceEqual(CurrentBlock.Header.Hash))
            {
                Console.WriteLine("Invalid Previous Block Hash");
                Console.WriteLine("PreviusBlockHash " + Base58.Bitcoin.Encode(new Span<Byte>(block.Header.PreviousBlockHash)));
                Console.WriteLine("LastBlockHash " + Base58.Bitcoin.Encode(new Span<Byte>(CurrentBlock.Header.Hash)));

                return null;
            }

            Console.WriteLine("Block Header validated.");

            var transact = TransactManager;
            var result = false;
            byte[] root = new byte[0];
            Console.WriteLine("Validatig deposits");

            var deposit = new Immutable.Deposit(DepositManager, transact);
            if (block.Deposits != null && block.DepositsDictionary.Count > 0)
            {
                foreach (var dp in block.Deposits)
                {
                    if (CancelToken.IsCancellationRequested)
                        return null;

                    if (!await Pool.Contains(dp))
                    {
                        Console.WriteLine("Block with invalid deposit");
                        return null;
                    }

                    deposit = deposit.ProcessDeposit(dp);
                    root = CryptoHelper.Hash(Base58.Bitcoin.Encode(new Span<Byte>(root)) + dp.ToString());
                }
            }
            transact = deposit.TransactManager;

            Console.WriteLine("Deposits validated");

            Console.WriteLine("Validating book");
            var book = new Book(BookManager, transact);
            if (block.OffersDictionary.Count > 0)
            {
                foreach (var of in block.Offers)
                {
                    if (CancelToken.IsCancellationRequested)
                        return null;

                    if (!await Pool.Contains(of))
                    {
                        Console.WriteLine("Block with invalid offer");
                        return null;
                    }

                    var clone = of.Clone();
                    clone.CleanTrades();

                    (result, book) = book.ProcessOffer(clone);

                    if (!result)
                    {
                        Console.WriteLine("Block with invalid offer");
                        return null;
                    }

                    foreach (var t in of.Trades)
                    {
                        if (!clone.Trades.Any(c => c.Equals(t)))
                        {
                            Console.WriteLine("Block with invalid trade");
                            return null;
                        }
                    }

                    root = CryptoHelper.Hash(Base58.Bitcoin.Encode(new Span<Byte>(root)) + clone.ToString());
                }
            }
            transact = book.TransactManager;

            if (block.OffersCancelDictionary.Count > 0)
            {
                foreach (var of in block.OfferCancels)
                {
                    if (CancelToken.IsCancellationRequested)
                        return null;

                    if (!await Pool.Contains(of))
                    {
                        Console.WriteLine("Block with invalid offer cancel");
                        return null;
                    }

                    var clone = of.Clone();

                    (result, book) = book.ProcessOfferCancel(clone);

                    if (!result)
                    {
                        Console.WriteLine("Block with invalid offer cancel");
                        return null;
                    }

                    root = CryptoHelper.Hash(Base58.Bitcoin.Encode(new Span<Byte>(root)) + clone.ToString());
                }
            }
            transact = book.TransactManager;
            Console.WriteLine("Book validated, validating transactions");
            (result, transact, root) = await ValidateTransactions(root, block, transact);

            if (!result)
                return null;

            Console.WriteLine("Transactions validated, validating withdrawals");

            var withdrawal = new Immutable.Withdrawal(WithdrawalManager, transact);

            if (block.Withdrawals != null && block.WithdrawalsDictionary.Count > 0)
            {
                foreach (var wd in block.Withdrawals)
                {
                    if (CancelToken.IsCancellationRequested)
                        return null;

                    if (!await Pool.Contains(wd) && !withdrawal.CanProcess(wd))
                    {
                        Console.WriteLine("Block with invalid withdrawal.");
                        return null;
                    }

                    withdrawal = withdrawal.ProcessWithdrawal(wd);
                    root = CryptoHelper.Hash(Base58.Bitcoin.Encode(new Span<Byte>(root)) + wd.ToString());
                }
            }

            transact = withdrawal.TransactManager;

            Console.WriteLine("withdrawals validated");

            if (!block.Header.MerkleRoot.SequenceEqual(root))
            {
                Console.WriteLine("Block with invalid merkle root");
                return null;
            }

            Console.WriteLine("Merkle root validated, getting commit lock");

            try
            {
                await commitLocker.WaitAsync();
                Console.WriteLine("commit lock gained");
                if (!CancelToken.IsCancellationRequested)
                {
                    CancelToken.Cancel();
                    Console.WriteLine("Block references last block, appending");
                    Mining = false;
                    transact.Balance.BlockHash = block.HashStr;
                    book.BlockHash = block.HashStr;
                    return new Chain(this, transact, deposit, withdrawal, book, block, CurrentBlock, Pool);
                }
            }
            finally
            {
                commitLocker.Release();
            }

            return null;
        }


        public bool CanFork(Block block, int count = 0)
        {
            if (count > 20)
                return false;

            if (block.Header.PreviousBlockHash == null && block.TransactionsDictionary.Count == 0)
                return true;

            if (CurrentBlock != null && CurrentBlock.Header.Hash.SequenceEqual(block.Header.PreviousBlockHash))
                return true;

            if (Previous == null || CurrentBlock == null || LastBlock == null)
                return false;

            if (Previous != null)
                return Previous.CanFork(block, ++count);

            return false;
        }

        public async Task<Chain> Fork(Block block, Pool pool, bool first = true)
        {
            if (CurrentBlock != null &&
                    block.Header.PreviousBlockHash != null &&
                    CurrentBlock.Header.Hash.SequenceEqual(block.Header.PreviousBlockHash))
            {
                var newChain = new Chain(Previous, TransactManager, DepositManager, WithdrawalManager, BookManager, CurrentBlock, LastBlock, pool);
                await newChain.Initialize(minerWallet);
                Console.WriteLine("Forking from Depth " + CurrentBlock.Header.Depth);
                if (first)
                {
                    newChain = await newChain.Append(block);
                    newChain.CancelToken = new CancellationTokenSource();
                }

                return newChain;
            }

            Chain fork = null;
            List<Block> blocks = new List<Block>();

            if (Previous != null && CurrentBlock != null)
            {
                fork = await Previous.Fork(block, pool, false);

                var tx = CurrentBlock.Transactions.ToList();
                if (tx.Count > 0)
                    tx.RemoveAt(0);
                await fork.Pool.Add(CurrentBlock.Deposits);
                await fork.Pool.Add(CurrentBlock.Offers);
                await fork.Pool.Add(tx);
                await fork.Pool.Add(CurrentBlock.Withdrawals);
                Console.WriteLine(tx.Count + " transactions added to fork");
                Console.WriteLine("Depth " + CurrentBlock.Header.Depth);
                Console.WriteLine("Block " + CurrentBlock.HashStr);
            }
            else
            {
                Console.WriteLine("Returning empty fork");
                fork = new Chain();
                fork.Pool = pool;
                await fork.Initialize(minerWallet);
            }

            return fork;
        }


        public async Task<Chain> Append(Block block)
        {
            try
            {
                Console.WriteLine("Starting process block");
                var result = await ProcessBlock(block);
                if (result == null)
                {
                    Console.WriteLine("Received Invalid block");
                    return null;
                }

                return result;
            }

            catch (Exception e)
            {
                Console.WriteLine("Error Appending Block");
                Console.WriteLine(e.ToString());
            }

            return await Task.FromResult<Chain>(null);
        }

        public Chain Cleanup()
        {
            var result = Get(39);
            if (result != null)
            {
                var cleanResult = result.Previous;
                result.Previous = null;
                result = cleanResult;
            }
            
            return result;
        }

        public Chain Get(int limit, int count = 0)
        {
            if (count < limit && Previous != null && Previous.CurrentBlock != null)
                return Previous.Get(limit, ++count);
            else if (count >= limit && Previous != null && Previous.CurrentBlock != null)
                return Previous;
            else
                return null;
        }

        public IDictionary<string, decimal> GetBalance(string address)
        {
            if (TransactManager.Balance.WalletDictionary.ContainsKey(address))
                return TransactManager.Balance.WalletDictionary[address].Balance;
            else
                return new Dictionary<string, decimal>();
        }
    }
}