using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

using Atomex;
using atomex_frontend.atomex_data_structures;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Services;
using Atomex.TezosTokens;
using Atomex.Wallet;
using Atomex.Wallet.Tezos;

namespace atomex_frontend.Storages
{
    public class TezosTokenViewModel
    {
        private bool _isPreviewDownloading;
        public TezosConfig TezosConfig { get; set; }
        public TokenBalance TokenBalance { get; set; }
        public string Address { get; set; }
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
                
                _isPreviewDownloading = true;

                _ = Task.Run(async () =>
                {
                    await LoadPreview()
                        .ConfigureAwait(false);

                    if (_tokenPreview != null)
                        PreviewLoaded?.Invoke();
                });

                return null;
            }
        }

        private async Task LoadPreview()
        {
            var thumbsApiSettings = new ThumbsApiSettings
            {
                ThumbsApiUri   = TezosConfig.ThumbsApiUri,
                IpfsGatewayUri = TezosConfig.IpfsGatewayUri,
                CatavaApiUri   = TezosConfig.CatavaApiUri
            };

            var thumbsApi = new ThumbsApi(thumbsApiSettings);

            foreach (var url in thumbsApi.GetTokenPreviewUrls(TokenBalance.Contract, TokenBalance.ThumbnailUri, TokenBalance.DisplayUri))
            {
                await FromUrlAsync(url)
                    .ConfigureAwait(false);

                if (_tokenPreview != null)
                    break;
            }

            _isPreviewDownloading = false;
        }

        public string Balance => TokenBalance.Balance != "1"
            ? $"{TokenBalance.GetTokenBalance().ToString(CultureInfo.InvariantCulture)}  {TokenBalance.Symbol}"
            : "";

        public bool IsIpfsAsset => TokenBalance.ArtifactUri != null && ThumbsApi.HasIpfsPrefix(TokenBalance.ArtifactUri);

        public string AssetUrl => IsIpfsAsset
            ? $"http://ipfs.io/ipfs/{ThumbsApi.RemoveIpfsPrefix(TokenBalance.ArtifactUri)}"
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
    }

    public class TezosTokenContractViewModel
    {
        public Action UIRefresh;
        public TokenContract Contract { get; set; }
        public string IconUrl => $"https://services.tzkt.io/v1/avatars/{Contract.Address}";
        public bool IsFa12 => Contract.GetContractType() == "FA12";
        public bool IsFa2 => Contract.GetContractType() == "FA2";
        
        private bool _isTriedToGetFromTzkt;
        private string _name;
        public string Name
        {
            get
            {
                if (_name != null)
                    return _name;

                if (!_isTriedToGetFromTzkt)
                {
                    _isTriedToGetFromTzkt = true;
                    _ = TryGetAliasAsync();
                }

                _name = Contract.Name;
                return _name;
            }
        }

        private async Task TryGetAliasAsync()
        {
            try
            {
                var response = await HttpHelper.HttpClient
                    .GetAsync($"https://api.tzkt.io/v1/accounts/{Contract.Address}")
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return;

                var stringResponse = await response.Content
                    .ReadAsStringAsync()
                    .ConfigureAwait(false);

                var alias = JsonConvert.DeserializeObject<JObject>(stringResponse)
                    ?["alias"]
                    ?.Value<string>();

                if (alias != null)
                    _name = alias;

                UIRefresh?.Invoke();
            }
            catch (Exception e)
            {
                Log.Error(e, "Alias getting error.");
            }
        }
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
                
                TokenContractChanged(TokenContract);
            }
        }

        public bool HasTokenContract => TokenContract != null;
        public bool IsFa12 => TokenContract?.IsFa12 ?? false;
        public bool IsFa2 => TokenContract?.IsFa2 ?? false;
        public bool LastWasFa2 { get; set; }
        
        public bool IsConvertable => _app.Account.Currencies
            .Any(c => c is Fa12Config fa12 && fa12.TokenContractAddress == TokenContractAddress);

        public string TokenContractAddress => TokenContract?.Contract?.Address ?? "";
        public string TokenContractName => TokenContract?.Name ?? "";

        public decimal Balance { get; set; }
        public string BalanceFormat { get; set; }
        public string BalanceCurrencyCode { get; set; }

        private readonly IAtomexApp _app;

        private bool _isBalanceUpdaing;

        public bool IsBalanceUpdating
        {
            get => _isBalanceUpdaing;
            set
            {
                _isBalanceUpdaing = value;
                CallUIRefresh();
            }
        }
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

        private readonly WalletStorage _ws;

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
                .Select(c => new TezosTokenContractViewModel
                {
                    UIRefresh = CallUIRefresh,
                    Contract = c
                });

            if (TokensContracts != null)
            {
                // add new token contracts if exists
                var newTokenContracts = tokensContractsViewModels.Except(
                    second: TokensContracts,
                    comparer: new Atomex.Common.EqualityComparer<TezosTokenContractViewModel>(
                        (x, y) => x.Contract.Address.Equals(y.Contract.Address),
                        x => x.Contract.Address.GetHashCode()));
                
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
                    // update current token contract
                    if (TokenContract != null)
                        TokenContractChanged(TokenContract);
                }
            }
            else
            {

                TokensContracts = new ObservableCollection<TezosTokenContractViewModel>(tokensContractsViewModels);
                
                TokenContract = TokensContracts.FirstOrDefault();
            }
        }

        private async void TokenContractChanged(TezosTokenContractViewModel tokenContract)
        {
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
                        .GetTezosTokenTransfersAsync(tokenContract.Contract.Address, offset: 0, limit: int.MaxValue))
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
                        .GetTezosTokenTransfersAsync(tokenContract.Contract.Address, offset: 0, limit: int.MaxValue))
                    .OrderByDescending(t => t.CreationTime)
                    .Select(t => new TezosTokenTransferViewModel(t, tezosConfig)));
                

                Tokens = new ObservableCollection<TezosTokenViewModel>(tokenAddresses
                    .Where(a => a.Balance != 0)
                    .Select(a => new TezosTokenViewModel
                    {
                        TezosConfig   = tezosConfig,
                        TokenBalance  = a.TokenBalance,
                        PreviewLoaded = CallUIRefresh,
                        Address       = a.Address,
                    })
                    .OrderBy(a => a.TokenBalance.TokenId)
                );
            }
            
            if (IsFa12)
                CurrentVariant = Variant.Transfers;

            if (IsFa2 && !LastWasFa2)
                CurrentVariant = Variant.Tokens;

            _ws.SelectedTezTokenContractAddress = TokenContractAddress;
            _ws.SelectedTezTokenContractIsFa12 = IsFa12;
            
            _ws.OpenedTx = null;
            CallUIRefresh();
        }
        
        public async void OnUpdateClick()
        {
            if (IsBalanceUpdating)
                return;

            IsBalanceUpdating = true;

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
                Log.Debug("Tezos tokens update operation canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "Exception during update TezosTokens");
                // todo: message to user!?
            }
            
            IsBalanceUpdating = false;
        }
    }
}