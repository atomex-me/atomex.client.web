using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Atomex;
using atomex_frontend.atomex_data_structures;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Services;
using Atomex.Wallet;
using Atomex.Wallet.Tezos;
using Serilog;

namespace atomex_frontend.Storages
{
    public class TezosTokenViewModel
    {
        private bool _isPreviewDownloading = false;

        public TokenBalance TokenBalance { get; set; }

        public Action PreviewLoaded;

        private string _tokenPreview;
        

        public string TokenPreview
        {
            get
            {
                if (_isPreviewDownloading)
                    return null;

                if (_tokenPreview != null)
                    return _tokenPreview;

                _ = Task.Run(async () =>
                {
                    await LoadPreview().ConfigureAwait(false);

                    if (_tokenPreview != null)
                        PreviewLoaded?.Invoke();
                });

                return null;
            }
        }

        private async Task LoadPreview()
        {
            _isPreviewDownloading = true;
            foreach (var url in GetTokenPreviewUrls())
            {
                await FromUrlAsync(url).ConfigureAwait(false);
                if (_tokenPreview != null) break;
            }
            _isPreviewDownloading = false;
        }

        public string Balance => TokenBalance.Balance != "1"
            ? $"{TokenBalance.Balance}  {TokenBalance.Symbol}"
            : "";

        public bool IsIpfsAsset => TokenBalance.ArtifactUri != null && HasIpfsPrefix(TokenBalance.ArtifactUri);

        public string AssetUrl => IsIpfsAsset
            ? $"http://ipfs.io/ipfs/{RemoveIpfsPrefix(TokenBalance.ArtifactUri)}"
            : null;

        private async Task FromUrlAsync(string url)
        {
            try
            {
                var response = await HttpHelper.HttpClient
                    .GetAsync(url)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var previewBytes = await response.Content
                        .ReadAsByteArrayAsync()
                        .ConfigureAwait(false);
                    
                    _tokenPreview = $"data:image/png;base64, {Convert.ToBase64String(previewBytes)}";
                }
            }
            catch
            {
                // ignored
            }
        }

        public IEnumerable<string> GetTokenPreviewUrls()
        {
            yield return $"https://test.atomex.me/nft-static-asset/{TokenBalance.Contract}/{TokenBalance.TokenId}.png";

            if (TokenBalance.ArtifactUri != null && HasIpfsPrefix(TokenBalance.ArtifactUri))
                yield return $"https://api.dipdup.net/thumbnail/{RemoveIpfsPrefix(TokenBalance.ArtifactUri)}";

            yield return $"https://services.tzkt.io/v1/avatars/{TokenBalance.Contract}";
        }

        public static string RemovePrefix(string s, string prefix) =>
            s.StartsWith(prefix) ? s.Substring(prefix.Length) : s;

        public static string RemoveIpfsPrefix(string url) =>
            RemovePrefix(url, "ipfs://");

        public static bool HasIpfsPrefix(string url) =>
            url?.StartsWith("ipfs://") ?? false;
    }

    public class TezosTokenContractViewModel
    {
        public TokenContract Contract { get; set; }
        public string IconUrl => $"https://services.tzkt.io/v1/avatars/{Contract.Address}";
        public bool IsFa12 => Contract.GetContractType() == "FA12";
        public bool IsFa2 => Contract.GetContractType() == "FA2";
    }

    public class TezosTokenStorage
    {
        private const int MaxAmountDecimals = 9;
        private const string Fa12 = "FA12";

        public ObservableCollection<TezosTokenContractViewModel> TokensContracts { get; set; }

        public ObservableCollection<TezosTokenViewModel> Tokens { get; set; }

        public ObservableCollection<TezosTokenTransferViewModel> Transfers { get; set; }

        private TezosTokenContractViewModel _tokenContract;
        public TezosTokenContractViewModel TokenContract
        {
            get => _tokenContract;
            set
            {
                LastWasFa2 = _tokenContract?.IsFa2 ?? false;
                _tokenContract = value;
                
                Console.WriteLine($"Setting TokenContract to {value.Contract.Address}");
                TokenContractChanged(TokenContract);
            }
        }

        public bool HasTokenContract => TokenContract != null;
        public bool IsFa12 => TokenContract?.IsFa12 ?? false;
        public bool IsFa2 => TokenContract?.IsFa2 ?? false;
        public bool LastWasFa2 { get; set; }

        public string TokenContractAddress => TokenContract?.Contract?.Address ?? "";
        public string TokenContractName => TokenContract?.Contract?.Name ?? "";
        public string TokenContractIconUrl => TokenContract?.IconUrl;

        public decimal Balance { get; set; }
        public string BalanceFormat { get; set; }
        public string BalanceCurrencyCode { get; set; }

        private readonly IAtomexApp _app;
        
        private bool _isBalanceUpdating;
        private CancellationTokenSource _cancellation;
        
        public event Action UIRefresh;
        private void CallUIRefresh()
        {
            UIRefresh?.Invoke();
        }
        
        public enum Variant
        {
            Tokens,
            Transfers
        }

        public Variant CurrentVariant { get; set; } = Variant.Tokens;

        public void OnVariantClick(Variant variant)
        {
            CurrentVariant = variant;
        }

        private WalletStorage _ws;

        public TezosTokenStorage(AccountStorage accountStorage, WalletStorage ws)
        {
            _app = accountStorage.AtomexApp ?? throw new ArgumentNullException(nameof(_app));
            _ws = ws;

            SubscribeToUpdates();
            _ = ReloadTokenContractsAsync();
        }

        private void SubscribeToUpdates()
        {
            _app.AtomexClientChanged += OnAtomexClientChanged;
            _app.Account.BalanceUpdated += OnBalanceUpdatedEventHandler;
        }

        private void OnAtomexClientChanged(object sender, AtomexClientChangedEventArgs e)
        {
            Tokens?.Clear();
            Transfers?.Clear();
            TokensContracts?.Clear();
            TokenContract = null;
        }

        protected virtual void OnBalanceUpdatedEventHandler(object sender, CurrencyEventArgs args)
        {
            try
            {
                if (Currencies.IsTezosToken(args.Currency))
                {
                    Task.Run(async () => await ReloadTokenContractsAsync());
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Account balance updated event handler error");
            }
        }

        private async Task ReloadTokenContractsAsync()
        {
            var tokensContractsViewModels = (await _app.Account
                    .GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz)
                    .DataRepository
                    .GetTezosTokenContractsAsync())
                .Select(c => new TezosTokenContractViewModel {Contract = c});

            if (TokensContracts != null)
            {
                // add new token contracts if exists
                var newTokenContracts = tokensContractsViewModels.Except(
                    second: TokensContracts,
                    comparer: new Atomex.Common.EqualityComparer<TezosTokenContractViewModel>(
                        (x, y) => x.Contract.Address.Equals(y.Contract.Address),
                        x => x.Contract.Address.GetHashCode()));
                
                Console.WriteLine($"ReloadTokenContractsAsync loaded token KTs {newTokenContracts.Count()}");

                if (newTokenContracts.Any())
                {
                    foreach (var newTokenContract in newTokenContracts)
                    {
                        TokensContracts.Add(newTokenContract);
                    }
                    
                    if (TokenContract == null)
                        TokenContract = TokensContracts.FirstOrDefault();
                }
                else
                {
                    Console.WriteLine($"newTokenContracts.Any() else {TokenContract}");
                    // update current token contract
                    if (TokenContract != null)
                        TokenContractChanged(TokenContract);
                }
            }
            else
            {
                Console.WriteLine($"tokensContractsViewModels count {tokensContractsViewModels.Count()}");
                TokensContracts = new ObservableCollection<TezosTokenContractViewModel>(tokensContractsViewModels);
                
                TokenContract = TokensContracts.FirstOrDefault();
            }
        }

        private async void TokenContractChanged(TezosTokenContractViewModel tokenContract)
        {
            Console.WriteLine($"Setting tokenContract to {tokenContract}");
            if (tokenContract == null)
            {
                Tokens = new ObservableCollection<TezosTokenViewModel>();
                Transfers = new ObservableCollection<TezosTokenTransferViewModel>();

                return;
            }

            var tezosConfig = _app.Account
                .Currencies
                .Get<TezosConfig>(TezosConfig.Xtz);

            if (tokenContract.IsFa12)
            {
                var tokenAccount = _app.Account.GetTezosTokenAccount<Fa12Account>(
                    currency: Fa12,
                    tokenContract: tokenContract.Contract.Address,
                    tokenId: 0);

                var tokenAddresses = await tokenAccount
                    .DataRepository
                    .GetTezosTokenAddressesByContractAsync(tokenContract.Contract.Address);

                var tokenAddress = tokenAddresses.FirstOrDefault();

                Balance = tokenAccount
                    .GetBalance()
                    .Available;

                BalanceFormat = tokenAddress?.TokenBalance != null
                    ? $"F{Math.Min(tokenAddress.TokenBalance.Decimals, MaxAmountDecimals)}"
                    : $"F{MaxAmountDecimals}";

                BalanceCurrencyCode = tokenAddress?.TokenBalance != null
                    ? tokenAddress.TokenBalance.Symbol
                    : "";

                Transfers = new ObservableCollection<TezosTokenTransferViewModel>((await tokenAccount
                        .DataRepository
                        .GetTezosTokenTransfersAsync(tokenContract.Contract.Address))
                    .OrderByDescending(t => t.CreationTime)
                    .Select(t => new TezosTokenTransferViewModel(t, tezosConfig)));

                Tokens = new ObservableCollection<TezosTokenViewModel>();
            }
            else if (tokenContract.IsFa2)
            {
                var tezosAccount = _app.Account
                    .GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz);

                var tokenAddresses = await tezosAccount
                    .DataRepository
                    .GetTezosTokenAddressesByContractAsync(tokenContract.Contract.Address);

                Transfers = new ObservableCollection<TezosTokenTransferViewModel>((await tezosAccount
                        .DataRepository
                        .GetTezosTokenTransfersAsync(tokenContract.Contract.Address))
                    .OrderByDescending(t => t.CreationTime)
                    .Select(t => new TezosTokenTransferViewModel(t, tezosConfig)));

                foreach (var ta in tokenAddresses)
                {
                    Console.WriteLine($"token address {ta.Address}");
                }

                Tokens = new ObservableCollection<TezosTokenViewModel>(tokenAddresses
                    .Select(a => new TezosTokenViewModel
                    {
                        TokenBalance = a.TokenBalance,
                        PreviewLoaded = CallUIRefresh
                    }));
            }

            Console.WriteLine($"Transfers: {Transfers.Count}");
            foreach (var transfer in Transfers)
            {
                Console.WriteLine(transfer.Id);
            }
            
            Console.WriteLine($"Tokens: {Tokens.Count}");
            foreach (var token in Tokens)
            {
                Console.WriteLine(token.AssetUrl);
            }

            if (IsFa12)
                CurrentVariant = Variant.Transfers;

            if (IsFa2 && !LastWasFa2)
                CurrentVariant = Variant.Tokens;


            _ws.OpenedTx = null;
            CallUIRefresh();
        }
        
        public async void OnUpdateClick()
        {
            if (_isBalanceUpdating)
                return;

            _isBalanceUpdating = true;

            _cancellation = new CancellationTokenSource();

            Console.WriteLine("Tokens balance updating...");

            try
            {
                var tezosAccount = _app.Account
                    .GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz);

                var tezosTokensScanner = new TezosTokensScanner(tezosAccount);

                await tezosTokensScanner.ScanAsync(
                    skipUsed: false,
                    cancellationToken: _cancellation.Token);

                // reload balances for all tezos tokens account
                foreach (var currency in _app.Account.Currencies)
                {
                    if (Currencies.IsTezosToken(currency.Name))
                        _app.Account
                            .GetCurrencyAccount<TezosTokenAccount>(currency.Name)
                            .ReloadBalances();
                }

                Console.WriteLine("Tokens balance updating finished!");
            }
            catch (OperationCanceledException)
            {
                Log.Debug("Wallet update operation canceled");
                Console.WriteLine("Wallet update operation canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "WalletViewModel.OnUpdateClick");
                Console.WriteLine("WalletViewModel.OnUpdateClick");
                // todo: message to user!?
            }
            

            _isBalanceUpdating = false;
        }
    }
}