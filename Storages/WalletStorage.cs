using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Atomex;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using atomex_frontend.atomex_data_structures;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Blockchain.Ethereum;
using Atomex.EthereumTokens;
using Atomex.Wallet.BitcoinBased;
using Atomex.TezosTokens;
using atomex_frontend.Common;
using Microsoft.AspNetCore.Components;
using System.Timers;
using System.Globalization;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Tezos;
using Newtonsoft.Json;
using Serilog;


namespace atomex_frontend.Storages
{
    public class WalletStorage
    {
        public WalletStorage(
            AccountStorage accountStorage,
            BakerStorage bakerStorage,
            NavigationManager uriHelper,
            Toolbelt.Blazor.I18nText.I18nText I18nText,
            IJSRuntime JSRuntime)
        {
            this.jSRuntime = JSRuntime;
            this.accountStorage = accountStorage;
            this.bakerStorage = bakerStorage;
            this.URIHelper = uriHelper;

            this.accountStorage.InitializeCallback += Initialize;

            debounceFirstCurrencySelection = new System.Timers.Timer(1);
            debounceFirstCurrencySelection.Elapsed += OnAfterChangeFirstCurrency;
            debounceFirstCurrencySelection.AutoReset = false;

            debounceSecondCurrencySelection = new System.Timers.Timer(1);
            debounceSecondCurrencySelection.Elapsed += OnAfterChangeSecondCurrency;
            debounceSecondCurrencySelection.AutoReset = false;
            LoadTranslations(I18nText);
        }

        I18nText.Translations Translations = new I18nText.Translations();

        private async void LoadTranslations(Toolbelt.Blazor.I18nText.I18nText I18nText)
        {
            Translations = await I18nText.GetTextTableAsync<I18nText.Translations>(null);
        }

        private IJSRuntime jSRuntime;
        private NavigationManager URIHelper;
        public event Action RefreshRequested;

        private void CallUIRefresh()
        {
            RefreshRequested?.Invoke();
        }

        public event Action CloseModals;

        private void CallCloseModals()
        {
            CloseModals?.Invoke();
        }

        public event Action<bool> RefreshMarket;

        private void CallMarketRefresh(bool reset = false)
        {
            RefreshMarket?.Invoke(reset);
        }

        private AccountStorage accountStorage;
        private BakerStorage bakerStorage;

        public List<CurrencyConfig> AvailableCurrencies
        {
            get
            {
                try
                {
                    return accountStorage.Account.Currencies.ToList();
                }
                catch (Exception e)
                {
                    return new List<CurrencyConfig>();
                }
            }
        }

        public List<KeyValuePair<string, string>> AvailableCurrenciesNames
        {
            get
            {
                try
                {
                    var result = accountStorage.Account.Currencies
                        .Select(c => new KeyValuePair<string, string>(c.Name, c.Description))
                        .ToList();
                    result.Add(new KeyValuePair<string, string>(TezosTokensCaption, TezosTokensCaption));
                    return result;
                }
                catch (Exception)
                {
                    return new List<KeyValuePair<string, string>>();
                }
            }
        }

        public string TezosTokensCaption => "Tezos Tokens";


        private bool _isTezosTokensSelected;

        public bool IsTezosTokensSelected
        {
            get => _isTezosTokensSelected;
            set
            {
                if (value != _isTezosTokensSelected)
                {
                    _isTezosTokensSelected = value;
                    OpenedTx = null;
                    CallUIRefresh();
                }
            }
        }

        public Dictionary<string, CurrencyData> PortfolioData { get; set; } = new Dictionary<string, CurrencyData>();

        public Dictionary<string, Transaction> Transactions { get; set; } = new Dictionary<string, Transaction>();

        public Dictionary<string, TezosTokenTransferViewModel> TezTokenTransfers { get; set; } =
            new Dictionary<string, TezosTokenTransferViewModel>();

        public List<Transaction> SelectedCurrencyTransactions
        {
            get => Transactions
                .Where(kvp =>
                {
                    var currName = kvp.Key.Split(Convert.ToChar("/"))[1];
                    return currName == SelectedCurrency.Name;
                })
                .Select(kvp => kvp.Value)
                .ToList()
                .OrderByDescending(a => a.CreationTime)
                .ToList();
        }

        public List<Transaction> SelectedCurrencyTezTokenTransfers
        {
            get => TezTokenTransfers
                .Where(kvp =>
                {
                    var currName = kvp.Key.Split(Convert.ToChar("/"))[1];
                    return currName == SelectedCurrency.Name;
                })
                .Select(kvp => (Transaction) kvp.Value)
                .ToList()
                .OrderByDescending(a => a.CreationTime)
                .ToList();
        }

        public decimal GetTotalDollars
        {
            get => Helper.SetPrecision(PortfolioData.Values.Sum(x => x.DollarValue), 1);
        }

        public CurrencyData SelectedCurrencyData
        {
            get => PortfolioData.TryGetValue(SelectedCurrency.Name, out CurrencyData data)
                ? data
                : new CurrencyData(accountStorage
                    .Account
                    .Currencies
                    .Get<CurrencyConfig>("BTC"), 0.0m, 0.0m, 0.0m);
        }

        public CurrencyData GetCurrencyData(string currencyName)
        {
            return PortfolioData.TryGetValue(currencyName, out CurrencyData data)
                ? data
                : new CurrencyData(accountStorage
                    .Account
                    .Currencies
                    .Get<CurrencyConfig>("BTC"), 0.0m, 0.0m, 0.0m);
        }

        private System.Timers.Timer debounceFirstCurrencySelection;

        private void OnAfterChangeFirstCurrency(Object source, ElapsedEventArgs e)
        {
            Task.Run(() =>
            {
                this.CheckForSimilarCurrencies();
                this.CallMarketRefresh(reset: true);
            });
        }

        public string SelectedTezTokenContractAddress { get; set; }
        public bool SelectedTezTokenContractIsFa12 { get; set; }

        private CurrencyConfig _selectedCurrency;

        public CurrencyConfig SelectedCurrency
        {
            get => _selectedCurrency;
            set
            {
                IsTezosTokensSelected = false;
                if (value.Name != _selectedCurrency.Name)
                {
                    ResetSendData();

                    if (CurrentWalletSection == WalletSection.DEX)
                    {
                        debounceFirstCurrencySelection.Stop();
                        debounceFirstCurrencySelection.Start();
                    }
                }

                _selectedCurrency = value;
                CallUIRefresh();
            }
        }

        private BitcoinBasedConfig BtcBased => SelectedCurrency as BitcoinBasedConfig;

        protected decimal _feeRate;

        public decimal FeeRate
        {
            get => _feeRate;
            set { _feeRate = value; }
        }

        private System.Timers.Timer debounceSecondCurrencySelection;

        private void OnAfterChangeSecondCurrency(Object source, ElapsedEventArgs e)
        {
            Task.Run(() =>
            {
                this.CallUIRefresh();
                this.CallMarketRefresh();
            });
        }

        private CurrencyConfig _selectedSecondCurrency;

        public CurrencyConfig SelectedSecondCurrency
        {
            get => _selectedSecondCurrency;
            set
            {
                if (this._selectedSecondCurrency.Name != value.Name)
                {
                    this._selectedSecondCurrency = value;

                    debounceSecondCurrencySelection.Stop();
                    debounceSecondCurrencySelection.Start();
                }
            }
        }

        private decimal _sendingAmount = 0;

        public decimal SendingAmount
        {
            get => this._sendingAmount;
            set
            {
                if (value >= 0)
                {
                    this.UpdateSendingAmount(value);
                }
            }
        }

        public string SendingToAddress { get; set; } = "";

        private decimal _sendingFee = 0;

        public decimal SendingFee
        {
            get => this._sendingFee;
            set
            {
                if (value >= 0)
                {
                    this.UpdateSendingFee(value);
                }
            }
        }

        private decimal _sendingFeePrice = 1;

        public decimal SendingFeePrice
        {
            get => this._sendingFeePrice;
            set
            {
                if (value >= 0)
                {
                    UpdateFeePrice(value);
                }
            }
        }


        private decimal _ethTotalFee = 0;

        public decimal TotalFee
        {
            get => GetEthreumBasedCurrency
                ? _ethTotalFee
                : this.SelectedCurrency.GetFeeAmount(_sendingFee, _sendingFeePrice);
        }

        private bool _useDefaultFee = true;

        public bool UseDefaultFee
        {
            get => _useDefaultFee;
            set
            {
                _useDefaultFee = value;
                SendingAmount = _sendingAmount;
            }
        }

        public string GasLimitString
        {
            get => Helper.DecimalToStr(this._sendingFee, "F0");
        }

        public string GasPriceString
        {
            get => Helper.DecimalToStr(this._sendingFeePrice);
        }

        public decimal SendingAmountDollars
        {
            get => Helper.SetPrecision(this.GetDollarValue(this.SelectedCurrency.Name, this._sendingAmount), 2);
        }

        public bool GetEthreumBasedCurrency
        {
            get => this.SelectedCurrency is EthereumConfig || this.SelectedCurrency is Erc20Config;
        }

        private bool _isUpdating = false;

        public bool IsUpdating
        {
            get => this._isUpdating;
            set
            {
                this._isUpdating = value;
                this.CallUIRefresh();
            }
        }

        private bool _allPortfolioUpdating = false;

        public bool AllPortfolioUpdating
        {
            get => this._allPortfolioUpdating;
            set
            {
                this._allPortfolioUpdating = value;
                this.CallUIRefresh();
            }
        }

        protected string _warning;

        public string Warning
        {
            get => _warning;
            set { _warning = value; }
        }

        private bool _isFeeUpdating = false;

        private bool IsFeeUpdating
        {
            get => this._isFeeUpdating;
            set
            {
                this._isFeeUpdating = value;
                this.CallUIRefresh();
            }
        }

        private bool _isAmountUpdating = false;

        private bool IsAmountUpdating
        {
            get => this._isAmountUpdating;
            set
            {
                this._isAmountUpdating = value;
                this.CallUIRefresh();
            }
        }

        public string lastSwapFromCurrencyName = "XTZ";

        private WalletSection _currentWalletSection = WalletSection.Portfolio;

        public WalletSection CurrentWalletSection
        {
            get => _currentWalletSection;
            set
            {
                _currentWalletSection = value;

                if (_currentWalletSection == WalletSection.DEX)
                {
                    if (IsTezosTokensSelected && SelectedTezTokenContractIsFa12)
                    {
                        _selectedCurrency = accountStorage.Account.Currencies
                            .FirstOrDefault(c =>
                                c is Fa12Config fa12 && fa12.TokenContractAddress == SelectedTezTokenContractAddress);
                    }
                    else
                    {
                        _selectedCurrency = accountStorage.Account.Currencies.Get<CurrencyConfig>(lastSwapFromCurrencyName);
                    }
                    
                    CheckForSimilarCurrencies();
                    CallMarketRefresh();
                }
                else
                {
                    lastSwapFromCurrencyName = SelectedCurrency.Name;
                }

                if (_currentWalletSection == WalletSection.Wallets)
                {
                    _ = bakerStorage.LoadDelegationInfoAsync();
                }

                if (_currentWalletSection == WalletSection.BuyWithCard)
                {
                    this.CallUIRefresh();
                }
            }
        }

        public ReceiveViewModel TezosReceiveVM { get; set; }

        public bool CanBuySelectedCurrency
        {
            get => SelectedCurrency?.Name == "XTZ" || SelectedCurrency?.Name == "ETH" ||
                   SelectedCurrency?.Name == "BTC";
        }

        public Transaction OpenedTx { get; set; }

        public UserMessage userMessage { get; set; } = null;

        public async void Initialize(bool IsRestarting)
        {
            if (accountStorage.AtomexApp != null)
            {
                if (accountStorage.AtomexApp.HasQuotesProvider && !IsRestarting)
                {
                    accountStorage.AtomexApp.QuotesProvider.QuotesUpdated +=
                        async (object sender, EventArgs args) => await UpdatedQuotes();
                }

                accountStorage.AtomexApp.Account.BalanceUpdated += async (sender, args) =>
                {
                    if (args.Currency == TezosConfig.Xtz)
                    {
                        _ = bakerStorage.LoadDelegationInfoAsync();
                        var tezosConfig = accountStorage.Account
                            .Currencies
                            .GetByName(TezosConfig.Xtz);
                        TezosReceiveVM = new ReceiveViewModel(accountStorage.AtomexApp, tezosConfig);
                    }

                    await BalanceUpdatedHandler(args.Currency);
                };

                accountStorage.AtomexApp.CurrenciesProvider.Updated += (sender, args) =>
                {
                    if (SelectedCurrency != null)
                    {
                        _selectedCurrency =
                            accountStorage.Account.Currencies.Get<CurrencyConfig>(SelectedCurrency.Name);
                    }

                    if (_selectedSecondCurrency != null)
                    {
                        _selectedSecondCurrency =
                            accountStorage.Account.Currencies.Get<CurrencyConfig>(SelectedSecondCurrency.Name);
                    }
                };

                accountStorage.AtomexApp.Account.UnconfirmedTransactionAdded +=
                    OnUnconfirmedTransactionAddedEventHandler;

                List<CurrencyConfig> currenciesList = accountStorage.Account.Currencies.ToList();
                Transactions = new Dictionary<string, Transaction>();
                PortfolioData = new Dictionary<string, CurrencyData>();


                foreach (CurrencyConfig currencyConfig in currenciesList)
                {
                    CurrencyData initialCurrencyData = new CurrencyData(currencyConfig, 0, 0, 0.0m);
                    PortfolioData.Add(currencyConfig.Name, initialCurrencyData);
                }

                var tezosConfig = accountStorage.Account
                    .Currencies
                    .GetByName(TezosConfig.Xtz);
                TezosReceiveVM = new ReceiveViewModel(accountStorage.AtomexApp, tezosConfig);

                await bakerStorage.LoadBakerList();
                await UpdatePortfolioAtStart();

                _selectedCurrency = this.accountStorage.Account.Currencies.Get<CurrencyConfig>("BTC");
                _selectedSecondCurrency = this.accountStorage.Account.Currencies.Get<CurrencyConfig>("XTZ");

                if (accountStorage.LoadingUpdate && !accountStorage.LoadFromRestore)
                {
                    await ScanAllCurrencies();
                }

                URIHelper.NavigateTo("/wallet");
                accountStorage.WalletLoading = false;
                CurrentWalletSection = WalletSection.Portfolio;
                try
                {
                    int idleForWallet = await accountStorage
                        .localStorage
                        .GetItemAsync<int>($"idle_timeout_{accountStorage.CurrentWalletName}");

                    if (idleForWallet != 0)
                    {
                        accountStorage.IdleTimeoutToLogout = idleForWallet;
                    }

                    await jSRuntime.InvokeVoidAsync("walletLoaded", accountStorage.IdleTimeoutToLogout);

                    var userId = accountStorage.GetUserId();
                    var userMsg = await GetUserMessageFromServer(userId);
                    if (userMsg.Count > 0 && !userMsg[0].isReaded)
                    {
                        userMessage = userMsg[0];
                        this.CallUIRefresh();
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"walletStorage Initialize error {e.ToString()}");
                }

                if (accountStorage.LoadFromRestore)
                {
                    await ScanAllCurrencies();
                }
            }
        }


        private async Task<List<UserMessage>> GetUserMessageFromServer(string userId)
        {
            var result = await HttpHelper.GetAsync(
                    baseUri: "https://test.atomex.me/",
                    requestUri: $"usermessages/get_user_messages/?uid={userId}&format=json",
                    responseHandler: response =>
                        JsonConvert.DeserializeObject<List<UserMessage>>(response.Content.ReadAsStringAsync()
                            .WaitForResult()),
                    cancellationToken: new CancellationTokenSource().Token)
                .ConfigureAwait(false);

            return result;
        }

        public async Task MarkUserMsgReaded(int id)
        {
            var result = await HttpHelper.PostAsync(
                    baseUri: "https://test.atomex.me/",
                    content: null,
                    requestUri: $"usermessages/get_user_messages/{id}/mark_read/",
                    responseHandler: response => response,
                    cancellationToken: new CancellationTokenSource().Token)
                .ConfigureAwait(false);

            userMessage = null;
            this.CallUIRefresh();
        }

        private void OnUnconfirmedTransactionAddedEventHandler(object sender, TransactionEventArgs e)
        {
            Console.WriteLine($"New Transaction on {e.Transaction.Currency}, HANDLING with id  {e.Transaction.Id}");

            handleTransaction(e.Transaction);
            CallUIRefresh();
            //await jSRuntime.InvokeVoidAsync("showNotification", "You have new transaction", $"ID: {e.Transaction.Id}");
        }

        public decimal GetCurrencyData(CurrencyConfig currency, string dataType)
        {
            if (dataType == "balance")
            {
                return PortfolioData.TryGetValue(currency.Name, out CurrencyData currencyData)
                    ? currencyData.Balance
                    : 0.0m;
            }

            if (dataType == "dollars")
            {
                return PortfolioData.TryGetValue(currency.Name, out CurrencyData currencyData)
                    ? currencyData.DollarValue
                    : 0.0m;
            }

            if (dataType == "percent")
            {
                return PortfolioData.TryGetValue(currency.Name, out CurrencyData currencyData)
                    ? currencyData.Percent
                    : 0.0m;
            }

            return 0.0m;
        }

        public async Task ScanCurrencyAsync(CurrencyConfig currency, bool scanningAll = false)
        {
            if (!scanningAll)
            {
                IsUpdating = true;
            }

            try
            {
                if (currency is Fa12Config)
                {
                    await accountStorage.Account
                        .GetCurrencyAccount<Fa12Account>(currency.Name)
                        .UpdateBalanceAsync(default);
                }
                else
                {
                    await new HdWalletScanner(accountStorage.Account)
                        .ScanAsync(currency.Name)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error update {currency.Name}: {e}");
            }
            finally
            {
                if (!scanningAll)
                {
                    IsUpdating = false;
                }
            }
        }

        public async Task ScanAllCurrencies()
        {
            AllPortfolioUpdating = true;
            var currencies = accountStorage.Account.Currencies.ToList();
            await Task.WhenAll(currencies.Select(currency => ScanCurrencyAsync(currency, scanningAll: true)));
            AllPortfolioUpdating = false;
        }

        public async Task UpdatedQuotes()
        {
            foreach (var currencyConfig in accountStorage.Account.Currencies)
            {
                await CountCurrencyPortfolio(currencyConfig);
            }

            foreach (var currencyConfig in accountStorage.Account.Currencies)
            {
                RefreshCurrencyPercent(currencyConfig.Name);
            }

            this.CallUIRefresh();

            if (CurrentWalletSection == WalletSection.Portfolio)
            {
                await DrawDonutChart(updateData: true);
            }
        }

        public async Task BalanceUpdatedHandler(string currencyName)
        {
            var currency = accountStorage.Account.Currencies.GetByName(currencyName);

            if (currency == null) return;

            await CountCurrencyPortfolio(currency);
            await RefreshTransactions(currency.Name);
            RefreshCurrencyPercent(currency.Name);

            CallUIRefresh();

            if (CurrentWalletSection == WalletSection.Portfolio)
            {
                await DrawDonutChart(updateData: true);
            }
        }

        private async Task CountCurrencyPortfolio(CurrencyConfig currencyConfig)
        {
            Balance balance = (await accountStorage.Account.GetBalanceAsync(currencyConfig.Name));
            var availableBalance = balance.Available;

            if (!PortfolioData.TryGetValue(currencyConfig.Name, out CurrencyData currencyData))
            {
                PortfolioData.Add(currencyConfig.Name,
                    new CurrencyData(currencyConfig, availableBalance,
                        this.GetDollarValue(currencyConfig.Name, availableBalance), 0.0m));
            }
            else
            {
                currencyData.Balance = availableBalance;
                currencyData.DollarValue = this.GetDollarValue(currencyConfig.Name, availableBalance);
            }
        }

        private void RefreshCurrencyPercent(string currencyName)
        {
            CurrencyData currData;

            if (PortfolioData.TryGetValue(currencyName, out currData))
            {
                currData.Percent = this.GetTotalDollars != 0
                    ? Helper.SetPrecision(currData.DollarValue / this.GetTotalDollars * 100.0m, 2)
                    : 0;
            }
        }

        public async Task UpdatePortfolioAtStart()
        {
            List<CurrencyConfig> currenciesList = accountStorage.Account.Currencies.ToList();

            foreach (var currencyConfig in currenciesList)
            {
                if (currencyConfig is Fa12Config)
                {
                    await LoadTezTransfersAsync(currencyConfig as Fa12Config);
                }

                await CountCurrencyPortfolio(currencyConfig);
                await LoadTransactions(currencyConfig);
            }


            foreach (CurrencyData currencyData in PortfolioData.Values)
            {
                RefreshCurrencyPercent(currencyData.CurrencyConfig.Name);
            }

            this.CallUIRefresh();

            if (CurrentWalletSection == WalletSection.Portfolio)
            {
                await DrawDonutChart(updateData: true);
            }
        }

        public async Task DrawDonutChart(bool updateData = false)
        {
            if (accountStorage.Account == null)
            {
                return;
            }

            List<decimal> currenciesDollar = new List<decimal>();
            List<string> currenciesLabels = new List<string>();

            foreach (CurrencyConfig currencyConfig in accountStorage.Account.Currencies)
            {
                if (PortfolioData.TryGetValue(currencyConfig.Name, out CurrencyData currencyData))
                {
                    currenciesDollar.Add(currencyData.DollarValue);
                    currenciesLabels.Add(currencyData.CurrencyConfig.Description);
                }
                else
                {
                    currenciesDollar.Add(0);
                    currenciesLabels.Add(currencyConfig.Description);
                }
            }

            await jSRuntime.InvokeVoidAsync(updateData ? "updateChart" : "drawChart", currenciesDollar.ToArray(),
                currenciesLabels.ToArray(), GetTotalDollars);
        }

        public async Task RefreshTransactions(string currency)
        {
            var currencyConfig = accountStorage.Account
                .Currencies
                .GetByName(currency);

            if (currencyConfig is Fa12Config)
            {
                await LoadTezTransfersAsync(currencyConfig as Fa12Config);
            }
            else
            {
                var transactions = await accountStorage.Account.GetTransactionsAsync(currency);
                foreach (var tx in transactions)
                {
                    var txAge = DateTime.Now - tx.CreationTime;
                    if (Transactions.ContainsKey($"{tx.Id}/{tx.Currency}") &&
                        tx.State == BlockchainTransactionState.Confirmed && txAge.Value.TotalDays > 1) continue;

                    handleTransaction(tx);
                }
            }
        }

        public async Task LoadTransactions(CurrencyConfig currencyConfig)
        {
            var transactions = await accountStorage.Account.GetTransactionsAsync(currencyConfig.Name);
            foreach (var tx in transactions)
            {
                handleTransaction(tx);
            }
        }

        private async Task LoadTezTransfersAsync(Fa12Config Currency)
        {
            Log.Debug("LoadTezTransfersAsync for {@currency}.", Currency.Name);

            try
            {
                if (accountStorage.Account == null)
                    return;

                var transfers = (await accountStorage.Account
                        .GetCurrencyAccount<Fa12Account>(Currency.Name)
                        .DataRepository
                        .GetTezosTokenTransfersAsync(Currency.TokenContractAddress)
                        .ConfigureAwait(false))
                    .ToList();

                var transfersList = new List<TezosTokenTransferViewModel>(
                    transfers.Select(t => new TezosTokenTransferViewModel(t, Currency))
                        .ToList()
                        .SortList((t1, t2) => t2.Time.CompareTo(t1.Time))
                        .ToList()
                );

                Console.WriteLine($"Finded {transfersList.Count} fa12 transfers;");

                foreach (var tx in transfersList)
                {
                    TezosTokenTransferViewModel oldTx;
                    string txKeyInDict = $"{tx.Id}/{tx.Currency}";

                    if (TezTokenTransfers.TryGetValue(txKeyInDict, out oldTx))
                    {
                        TezTokenTransfers[txKeyInDict] = tx;
                    }
                    else
                    {
                        TezTokenTransfers.Add(txKeyInDict, tx);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log.Debug("LoadTezTransfersAsync canceled.");
            }
            catch (Exception e)
            {
                Log.Error(e, "LoadTezTransfersAsync error for {@currency}.", Currency?.Name);
            }
        }


        private void AddTransaction(Transaction tx)
        {
            Transaction oldTx;
            string TxKeyInDict = $"{tx.Id}/{tx.Currency}";
            var currencyConfig = accountStorage.Account.Currencies.GetByName(tx.Currency);

            var type =
                tx.Type.HasFlag(BlockchainTransactionType.Input) ||
                tx.Type.HasFlag(BlockchainTransactionType.SwapRedeem) ? "income" :
                tx.Type.HasFlag(BlockchainTransactionType.Output) ? "outcome" : "";

            if (Transactions.TryGetValue(TxKeyInDict, out oldTx))
            {
                Transactions[TxKeyInDict] = tx;

                if (oldTx.State != BlockchainTransactionState.Confirmed &&
                    tx.State == BlockchainTransactionState.Confirmed && tx.Amount != 0 && !AllPortfolioUpdating &&
                    !IsUpdating)
                {
                    jSRuntime.InvokeVoidAsync("showNotification", $"{currencyConfig.Description} {type}",
                        tx.Description, $"/css/images/{currencyConfig.Description.ToLower()}_90x90.png");
                }
            }
            else
            {
                Transactions.Add(TxKeyInDict, tx);

                if (tx.State == BlockchainTransactionState.Confirmed && tx.Amount != 0 && !AllPortfolioUpdating &&
                    !IsUpdating)
                {
                    jSRuntime.InvokeVoidAsync("showNotification", $"{currencyConfig.Description} {type}",
                        tx.Description, $"/css/images/{currencyConfig.Description.ToLower()}_90x90.png");
                }
            }
        }

        public async void RemoveTransacton(string id, string currencyName)
        {
            if (accountStorage.AtomexApp.Account == null)
                return;

            try
            {
                var isRemoved = await accountStorage.AtomexApp.Account
                    .RemoveTransactionAsync($"{id}:{currencyName}");

                if (isRemoved)
                {
                    Transactions.Remove($"{id}/{currencyName}");
                }

                OpenedTx = null;
                this.CallUIRefresh();
            }
            catch (Exception e)
            {
                Log.Error(e, "Transaction remove error");
            }
        }

        private void handleTransaction(IBlockchainTransaction tx)
        {
            decimal amount;
            string description = "";
            var currencyConfig = accountStorage.Account.Currencies.GetByName(tx.Currency);

            switch (currencyConfig)
            {
                case BitcoinBasedConfig _:
                    IBitcoinBasedTransaction btcBasedTrans = (IBitcoinBasedTransaction) tx;
                    amount = CurrHelper.GetTransAmount(btcBasedTrans, currencyConfig);
                    var BtcFee = btcBasedTrans.Fees != null
                        ? btcBasedTrans.Fees.Value / currencyConfig.DigitsMultiplier
                        : 0;

                    AddTransaction(
                        new Transaction(
                            currencyConfig,
                            btcBasedTrans.Id,
                            btcBasedTrans.State,
                            btcBasedTrans.Type,
                            btcBasedTrans.CreationTime,
                            btcBasedTrans.IsConfirmed,
                            amount,
                            BtcFee
                        )
                    );
                    break;

                case Erc20Config _:
                    EthereumTransaction usdtTrans = (EthereumTransaction) tx;
                    amount = CurrHelper.GetTransAmount(usdtTrans, currencyConfig);
                    string FromUsdt = usdtTrans.From;
                    string ToUsdt = usdtTrans.To;
                    decimal GasPriceUsdt = EthereumConfig.WeiToGwei((decimal) usdtTrans.GasPrice);
                    decimal GasLimitUsdt = (decimal) usdtTrans.GasLimit;
                    decimal GasUsedUsdt = (decimal) usdtTrans.GasUsed;
                    bool IsInternalUsdt = usdtTrans.IsInternal;
                    AddTransaction(
                        new Transaction(
                            currencyConfig,
                            usdtTrans.Id,
                            usdtTrans.State,
                            usdtTrans.Type,
                            usdtTrans.CreationTime,
                            usdtTrans.IsConfirmed,
                            amount,
                            from: FromUsdt,
                            to: ToUsdt,
                            gasPrice: GasPriceUsdt,
                            gasLimit: GasLimitUsdt,
                            gasUsed: GasUsedUsdt,
                            isInternal: IsInternalUsdt
                        )
                    );
                    break;

                case EthereumConfig _:
                    EthereumTransaction ethTrans = (EthereumTransaction) tx;
                    amount = CurrHelper.GetTransAmount(ethTrans, currencyConfig);
                    string FromEth = ethTrans.From;
                    string ToEth = ethTrans.To;
                    decimal GasPriceEth = EthereumConfig.WeiToGwei((decimal) ethTrans.GasPrice);
                    decimal GasLimitEth = (decimal) ethTrans.GasLimit;
                    decimal GasUsedEth = (decimal) ethTrans.GasUsed;
                    decimal FeeEth = EthereumConfig.WeiToEth(ethTrans.GasUsed * ethTrans.GasPrice);
                    bool IsInternalEth = ethTrans.IsInternal;
                    AddTransaction(
                        new Transaction(
                            currencyConfig,
                            ethTrans.Id,
                            ethTrans.State,
                            ethTrans.Type,
                            ethTrans.CreationTime,
                            ethTrans.IsConfirmed,
                            amount,
                            fee: FeeEth,
                            from: FromEth,
                            to: ToEth,
                            gasPrice: GasPriceEth,
                            gasLimit: GasLimitEth,
                            gasUsed: GasUsedEth,
                            isInternal: IsInternalEth
                        )
                    );
                    break;

                // todo: remove
                case Fa12Config _:
                    TezosTransaction fa12Trans = (TezosTransaction) tx;
                    amount = CurrHelper.GetTransAmount(fa12Trans, currencyConfig);
                    string FromFa12 = fa12Trans.From;
                    string ToFa12 = fa12Trans.To;
                    decimal GasLimitFa12 = fa12Trans.GasLimit;
                    decimal FeeFa12 = TezosConfig.MtzToTz(fa12Trans.Fee);
                    bool IsInternalFa12 = fa12Trans.IsInternal;
                    AddTransaction(
                        new Transaction(
                            currencyConfig,
                            fa12Trans.Id,
                            fa12Trans.State,
                            fa12Trans.Type,
                            fa12Trans.CreationTime,
                            fa12Trans.IsConfirmed,
                            amount,
                            FeeFa12,
                            from: FromFa12,
                            to: ToFa12,
                            gasLimit: GasLimitFa12,
                            isInternal: IsInternalFa12,
                            alias: fa12Trans.Alias
                        )
                    );
                    break;

                case TezosConfig _:
                    TezosTransaction xtzTrans = (TezosTransaction) tx;
                    amount = CurrHelper.GetTransAmount(xtzTrans, currencyConfig);
                    decimal FeeXtz = CurrHelper.GetFee(xtzTrans);
                    string FromXtz = xtzTrans.From;
                    string ToXtz = xtzTrans.To;
                    decimal GasLimitXtz = xtzTrans.GasLimit;
                    bool IsInternalXtz = xtzTrans.IsInternal;
                    bool IsRewardTx = GetTezosTxIsReward(xtzTrans);
                    AddTransaction(
                        new Transaction(
                            currencyConfig,
                            xtzTrans.Id,
                            xtzTrans.State,
                            xtzTrans.Type,
                            xtzTrans.CreationTime,
                            xtzTrans.IsConfirmed,
                            amount,
                            FeeXtz,
                            from: FromXtz,
                            to: ToXtz,
                            gasLimit: GasLimitXtz,
                            isInternal: IsInternalXtz,
                            alias: xtzTrans.Alias,
                            isRewardTx: IsRewardTx
                        )
                    );
                    break;
            }
        }

        public decimal GetDollarValue(string currency, decimal amount)
        {
            if (accountStorage.QuotesProvider != null)
            {
                return Helper.SetPrecision(accountStorage.QuotesProvider.GetQuote(currency, "USD").Bid * amount, 4);
            }

            return 0;
        }

        protected async void UpdateSendingAmount(decimal amount)
        {
            if (IsAmountUpdating)
                return;

            if ((SelectedCurrency is Fa12Config || SelectedCurrency is TezosConfig) &&
                !SelectedCurrency.IsValidAddress(SendingToAddress))
            {
                CallUIRefresh();
                return;
            }

            var account = accountStorage.Account
                .GetCurrencyAccount<ILegacyCurrencyAccount>(SelectedCurrency.Name);

            Warning = string.Empty;

            _sendingAmount = amount;

            IsAmountUpdating = true;

            if (SelectedCurrency is BitcoinBasedConfig)
            {
                try
                {
                    if (UseDefaultFee)
                    {
                        var (maxAmount, _, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output);

                        if (_sendingAmount > maxAmount)
                        {
                            Warning = Translations.CvInsufficientFunds;
                            IsAmountUpdating = false;
                            return;
                        }

                        var estimatedFeeAmount = _sendingAmount != 0
                            ? await account.EstimateFeeAsync(SendingToAddress, _sendingAmount,
                                BlockchainTransactionType.Output)
                            : 0;

                        _sendingFee = estimatedFeeAmount ?? SelectedCurrency.GetDefaultFee();

                        FeeRate = await BtcBased.GetFeeRateAsync();
                    }
                    else
                    {
                        var availableAmount = SelectedCurrencyData.Balance;

                        if (_sendingAmount + _sendingFee > availableAmount)
                        {
                            Warning = Translations.CvInsufficientFunds;
                            IsAmountUpdating = false;
                            return;
                        }

                        SendingFee = _sendingFee;
                    }
                }
                finally
                {
                    IsAmountUpdating = false;
                }
            }
            else if (SelectedCurrency is EthereumConfig)
            {
                try
                {
                    if (UseDefaultFee)
                    {
                        var (maxAmount, maxFeeAmount, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output, 0, 0,
                                false);

                        _sendingFee = SelectedCurrency.GetDefaultFee();

                        _sendingFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();

                        if (_sendingAmount > maxAmount)
                        {
                            Warning = Translations.CvInsufficientFunds;
                            return;
                        }

                        UpdateTotalFeeString();
                    }
                    else
                    {
                        var (maxAmount, maxFeeAmount, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output,
                                _sendingFee, _sendingFeePrice, false);

                        if (_sendingAmount > maxAmount)
                        {
                            Warning = Translations.CvInsufficientFunds;
                            return;
                        }

                        if (_sendingFee < SelectedCurrency.GetDefaultFee() || _sendingFeePrice == 0)
                            Warning = Translations.CvLowFees;
                    }
                }
                finally
                {
                    IsAmountUpdating = false;
                }
            }
            else if (SelectedCurrency is Erc20Config)
            {
                try
                {
                    var availableAmount = SelectedCurrencyData.Balance;

                    if (UseDefaultFee)
                    {
                        var (maxAmount, maxFeeAmount, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output, 0, 0,
                                false);

                        _sendingFee = SelectedCurrency.GetDefaultFee();

                        _sendingFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();

                        if (_sendingAmount > maxAmount)
                        {
                            if (_sendingAmount <= availableAmount)
                                Warning = string.Format(CultureInfo.InvariantCulture,
                                    Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);
                            else
                                Warning = Translations.CvInsufficientFunds;

                            return;
                        }

                        UpdateTotalFeeString();
                    }
                    else
                    {
                        var (maxAmount, _, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output,
                                _sendingFee, _sendingFeePrice, false);

                        if (_sendingAmount > maxAmount)
                        {
                            if (_sendingAmount <= availableAmount)
                                Warning = string.Format(CultureInfo.InvariantCulture,
                                    Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);
                            else
                                Warning = Translations.CvInsufficientFunds;

                            return;
                        }

                        if (_sendingFee < SelectedCurrency.GetDefaultFee() || _sendingFeePrice == 0)
                            Warning = Translations.CvLowFees;
                    }
                }
                finally
                {
                    IsAmountUpdating = false;
                }
            }

            else if (SelectedCurrency is Fa12Config)
            {
                try
                {
                    var availableAmount = SelectedCurrencyData.Balance;
                    if (UseDefaultFee)
                    {
                        var (maxAmount, maxFeeAmount, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output, 0, 0,
                                false);

                        if (_sendingAmount > maxAmount)
                        {
                            if (_sendingAmount <= availableAmount)
                                Warning = string.Format(CultureInfo.InvariantCulture,
                                    Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);
                            else
                                Warning = Translations.CvInsufficientFunds;

                            return;
                        }

                        var estimatedFeeAmount = _sendingAmount != 0
                            ? await account.EstimateFeeAsync(SendingToAddress, _sendingAmount,
                                BlockchainTransactionType.Output)
                            : 0;

                        var defaultFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();
                        _sendingFee =
                            SelectedCurrency.GetFeeFromFeeAmount(estimatedFeeAmount ?? SelectedCurrency.GetDefaultFee(),
                                defaultFeePrice);
                    }
                    else
                    {
                        var (maxAmount, _, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output, 0, 0,
                                false);

                        if (_sendingAmount > maxAmount)
                        {
                            if (_sendingAmount <= availableAmount)
                                Warning = string.Format(CultureInfo.InvariantCulture,
                                    Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);
                            else
                                Warning = Translations.CvInsufficientFunds;

                            return;
                        }

                        SendingFee = _sendingFee;
                    }
                }
                finally
                {
                    IsAmountUpdating = false;
                }
            }

            else
            {
                try
                {
                    var defaultFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();

                    if (UseDefaultFee)
                    {
                        var (maxAmount, maxFeeAmount, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output, 0, 0,
                                true);

                        if (_sendingAmount > maxAmount)
                        {
                            Warning = Translations.CvInsufficientFunds;
                            return;
                        }

                        var estimatedFeeAmount = _sendingAmount != 0
                            ? await account.EstimateFeeAsync(SendingToAddress, _sendingAmount,
                                BlockchainTransactionType.Output)
                            : 0;

                        _sendingFee =
                            SelectedCurrency.GetFeeFromFeeAmount(estimatedFeeAmount ?? SelectedCurrency.GetDefaultFee(),
                                defaultFeePrice);
                    }
                    else
                    {
                        var (maxAmount, maxFeeAmount, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output, 0, 0,
                                false);

                        var availableAmount = SelectedCurrency is BitcoinBasedConfig
                            ? SelectedCurrencyData.Balance
                            : maxAmount + maxFeeAmount;

                        var feeAmount = SelectedCurrency.GetFeeAmount(_sendingFee, defaultFeePrice);

                        if (_sendingAmount > maxAmount || _sendingAmount + feeAmount > availableAmount)
                        {
                            Warning = Translations.CvInsufficientFunds;
                            return;
                        }

                        SendingFee = _sendingFee;
                    }
                }
                finally
                {
                    IsAmountUpdating = false;
                }
            }
        }

        protected async void UpdateTotalFeeString(decimal totalFeeAmount = 0)
        {
            try
            {
                var account = accountStorage.Account
                    .GetCurrencyAccount<ILegacyCurrencyAccount>(SelectedCurrency.Name);

                var feeAmount = totalFeeAmount > 0
                    ? totalFeeAmount
                    : SelectedCurrency.GetFeeAmount(_sendingFee, _sendingFeePrice) > 0
                        ? await account.EstimateFeeAsync(SendingToAddress, _sendingAmount,
                            BlockchainTransactionType.Output, _sendingFee, _sendingFeePrice)
                        : 0;

                if (feeAmount != null)
                {
                    _ethTotalFee = feeAmount.Value;
                }
            }
            catch
            {
            }
        }


        protected async void UpdateSendingFee(decimal fee)
        {
            if (IsFeeUpdating)
            {
                return;
            }

            this._sendingFee = fee;

            if (_sendingAmount == 0)
            {
                this.CallUIRefresh();
                return;
            }

            var account = accountStorage.Account
                .GetCurrencyAccount<ILegacyCurrencyAccount>(SelectedCurrency.Name);

            IsFeeUpdating = true;

            _sendingFee = Math.Min(fee, SelectedCurrency.GetMaximumFee());
            Warning = string.Empty;

            if (SelectedCurrency is BitcoinBasedConfig)
            {
                try
                {
                    var availableAmount = SelectedCurrencyData.Balance;

                    if (_sendingAmount == 0)
                    {
                        var defaultFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();

                        if (SelectedCurrency.GetFeeAmount(_sendingFee, defaultFeePrice) > availableAmount)
                            Warning = Translations.CvInsufficientFunds;

                        IsFeeUpdating = true;
                        return;
                    }
                    else if (_sendingAmount + _sendingFee > availableAmount)
                    {
                        Warning = Translations.CvInsufficientFunds;
                        IsFeeUpdating = false;
                        return;
                    }

                    var estimatedTxSize = await EstimateTxSizeAsync(_sendingAmount, _sendingFee);

                    if (estimatedTxSize == null || estimatedTxSize.Value == 0)
                    {
                        Warning = Translations.CvInsufficientFunds;
                        IsFeeUpdating = false;
                        return;
                    }

                    if (!UseDefaultFee)
                    {
                        var minimumFeeSatoshi = BtcBased.GetMinimumFee(estimatedTxSize.Value);
                        var minimumFee = BtcBased.SatoshiToCoin(minimumFeeSatoshi);

                        if (_sendingFee < minimumFee)
                            Warning = Translations.CvLowFees;
                    }

                    FeeRate = BtcBased.CoinToSatoshi(_sendingFee) / estimatedTxSize.Value;
                }
                finally
                {
                    IsFeeUpdating = false;
                }
            }

            else if (SelectedCurrency is Erc20Config)
            {
                try
                {
                    if (_sendingAmount == 0)
                    {
                        if (SelectedCurrency.GetFeeAmount(_sendingFee, _sendingFeePrice) > SelectedCurrencyData.Balance)
                            Warning = Translations.CvInsufficientFunds;

                        return;
                    }

                    if (_sendingFee < SelectedCurrency.GetDefaultFee())
                    {
                        Warning = Translations.CvLowFees;
                        if (fee == 0)
                        {
                            UpdateTotalFeeString();
                            return;
                        }
                    }

                    if (!UseDefaultFee)
                    {
                        var (maxAmount, maxFee, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output,
                                _sendingFee, _sendingFeePrice, false);

                        if (_sendingAmount > maxAmount)
                        {
                            var availableAmount = SelectedCurrencyData.Balance;

                            if (_sendingAmount <= availableAmount)
                                Warning = string.Format(CultureInfo.InvariantCulture,
                                    Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);
                            else
                                Warning = Translations.CvInsufficientFunds;

                            return;
                        }

                        UpdateTotalFeeString();
                    }
                }
                finally
                {
                    IsFeeUpdating = false;
                }
            }

            else if (SelectedCurrency is EthereumConfig)
            {
                try
                {
                    if (_sendingAmount == 0)
                    {
                        if (SelectedCurrency.GetFeeAmount(_sendingFee, _sendingFeePrice) > SelectedCurrencyData.Balance)
                            Warning = Translations.CvInsufficientFunds;

                        return;
                    }

                    if (_sendingFee < SelectedCurrency.GetDefaultFee())
                    {
                        Warning = Translations.CvLowFees;
                        if (fee == 0)
                        {
                            UpdateTotalFeeString();

                            return;
                        }
                    }

                    if (!UseDefaultFee)
                    {
                        var (maxAmount, maxFee, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output,
                                _sendingFee, _sendingFeePrice, false);

                        if (_sendingAmount > maxAmount)
                        {
                            Warning = Translations.CvInsufficientFunds;

                            return;
                        }

                        UpdateTotalFeeString();
                    }
                }
                finally
                {
                    IsFeeUpdating = false;
                }
            }

            else if (SelectedCurrency is Fa12Config)
            {
                try
                {
                    if (_sendingAmount == 0)
                    {
                        var defaultFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();

                        if (SelectedCurrency.GetFeeAmount(_sendingFee, defaultFeePrice) > SelectedCurrencyData.Balance)
                            Warning = Translations.CvInsufficientFunds;

                        return;
                    }

                    if (!UseDefaultFee)
                    {
                        var availableAmount = SelectedCurrencyData.Balance;

                        var (maxAmount, maxAvailableFee, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output,
                                decimal.MaxValue, 0, false);

                        var defaultFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();
                        var feeAmount = SelectedCurrency.GetFeeAmount(_sendingFee, defaultFeePrice);

                        var estimatedFeeAmount = _sendingAmount != 0
                            ? await account.EstimateFeeAsync(SendingToAddress, _sendingAmount,
                                BlockchainTransactionType.Output)
                            : 0;

                        if (_sendingAmount > maxAmount)
                        {
                            if (_sendingAmount <= availableAmount)
                                Warning = string.Format(CultureInfo.InvariantCulture,
                                    Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);
                            else
                                Warning = Translations.CvInsufficientFunds;

                            return;
                        }
                        else if (estimatedFeeAmount == null || feeAmount < estimatedFeeAmount.Value)
                        {
                            Warning = Translations.CvLowFees;
                        }

                        if (feeAmount > maxAvailableFee)
                            Warning = string.Format(CultureInfo.InvariantCulture, Translations.CvInsufficientChainFunds,
                                SelectedCurrency.FeeCurrencyName);
                    }
                }
                finally
                {
                    IsFeeUpdating = false;
                }
            }

            else
            {
                try
                {
                    var defaultFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();

                    if (_sendingAmount == 0)
                    {
                        if (SelectedCurrency.GetFeeAmount(_sendingFee, defaultFeePrice) > SelectedCurrencyData.Balance)
                            Warning = Translations.CvInsufficientFunds;

                        return;
                    }

                    if (!UseDefaultFee)
                    {
                        var estimatedFeeAmount = _sendingAmount != 0
                            ? await account.EstimateFeeAsync(SendingToAddress, _sendingAmount,
                                BlockchainTransactionType.Output)
                            : 0;

                        var (maxAmount, maxFeeAmount, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output, 0, 0,
                                false);

                        var availableAmount = SelectedCurrency is BitcoinBasedConfig
                            ? SelectedCurrencyData.Balance
                            : maxAmount + maxFeeAmount;

                        var feeAmount = SelectedCurrency.GetFeeAmount(_sendingFee, defaultFeePrice);

                        if (_sendingAmount + feeAmount > availableAmount)
                        {
                            Warning = Translations.CvInsufficientFunds;

                            return;
                        }
                        else if (estimatedFeeAmount == null || feeAmount < estimatedFeeAmount.Value)
                        {
                            Warning = Translations.CvLowFees;
                        }
                    }
                }
                finally
                {
                    IsFeeUpdating = false;
                }
            }
        }

        private async void UpdateFeePrice(decimal value)
        {
            if (IsFeeUpdating)
                return;

            var account = accountStorage.Account
                .GetCurrencyAccount<ILegacyCurrencyAccount>(SelectedCurrency.Name);

            IsFeeUpdating = true;

            _sendingFeePrice = value;

            Warning = string.Empty;

            if (SelectedCurrency is EthereumConfig)
            {
                try
                {
                    if (_sendingAmount == 0)
                    {
                        if (SelectedCurrency.GetFeeAmount(_sendingFee, _sendingFeePrice) > SelectedCurrencyData.Balance)
                            Warning = Translations.CvInsufficientFunds;
                        return;
                    }

                    if (value == 0)
                    {
                        Warning = Translations.CvLowFees;
                        UpdateTotalFeeString();
                        return;
                    }

                    if (!UseDefaultFee)
                    {
                        var (maxAmount, maxFee, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output,
                                _sendingFee, _sendingFeePrice, false);

                        if (_sendingAmount > maxAmount)
                        {
                            Warning = Translations.CvInsufficientFunds;
                            return;
                        }

                        UpdateTotalFeeString();
                    }
                }
                finally
                {
                    IsFeeUpdating = false;
                    this.CallUIRefresh();
                }
            }

            else if (SelectedCurrency is Erc20Config)
            {
                try
                {
                    if (_sendingAmount == 0)
                    {
                        if (SelectedCurrency.GetFeeAmount(_sendingFee, _sendingFeePrice) > SelectedCurrencyData.Balance)
                            Warning = Translations.CvInsufficientFunds;
                        return;
                    }

                    if (value == 0)
                    {
                        Warning = Translations.CvLowFees;
                        UpdateTotalFeeString();
                        return;
                    }

                    if (!UseDefaultFee)
                    {
                        var (maxAmount, maxFee, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output,
                                _sendingFee, _sendingFeePrice, false);

                        if (_sendingAmount > maxAmount)
                        {
                            var availableAmount = SelectedCurrencyData.Balance;

                            if (_sendingAmount <= availableAmount)
                                Warning = string.Format(CultureInfo.InvariantCulture,
                                    Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);
                            else
                                Warning = Translations.CvInsufficientFunds;
                            return;
                        }

                        UpdateTotalFeeString();
                    }
                }
                finally
                {
                    IsFeeUpdating = false;
                    this.CallUIRefresh();
                }
            }
        }

        public void OnNextCommand()
        {
            if (string.IsNullOrEmpty(SendingToAddress))
            {
                Warning = Translations.SvEmptyAddressError;
                return;
            }

            if (!SelectedCurrency.IsValidAddress(SendingToAddress))
            {
                Warning = Translations.SvInvalidAddressError;
                return;
            }

            if (SendingAmount <= 0)
            {
                Warning = Translations.SvAmountLessThanZeroError;
                return;
            }

            if (SendingFee <= 0)
            {
                Warning = Translations.SvCommissionLessThanZeroError;
                return;
            }

            var isToken = SelectedCurrency.FeeCurrencyName != SelectedCurrency.Name;

            var feeAmount = !isToken
                ? SelectedCurrency is EthereumConfig ? SelectedCurrency.GetFeeAmount(SendingFee, SendingFeePrice) :
                SendingFee
                : 0;

            if (SendingAmount + feeAmount > SelectedCurrencyData.Balance)
            {
                Warning = Translations.SvAvailableFundsError;
                return;
            }

            return;
        }

        public async void OnMaxClick()
        {
            if (IsAmountUpdating)
                return;

            var account = accountStorage.Account
                .GetCurrencyAccount<ILegacyCurrencyAccount>(SelectedCurrency.Name);

            IsAmountUpdating = true;

            Warning = string.Empty;

            if (SelectedCurrency is BitcoinBasedConfig)
            {
                try
                {
                    if (SelectedCurrencyData.Balance == 0)
                        return;

                    if (UseDefaultFee) // auto fee
                    {
                        var (maxAmount, maxFeeAmount, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output);

                        if (maxAmount > 0)
                            _sendingAmount = maxAmount;

                        var defaultFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();
                        _sendingFee = SelectedCurrency.GetFeeFromFeeAmount(maxFeeAmount, defaultFeePrice);

                        FeeRate = await BtcBased.GetFeeRateAsync();
                    }
                    else // manual fee
                    {
                        var availableAmount = SelectedCurrencyData.Balance;

                        if (availableAmount - _sendingFee > 0)
                        {
                            _sendingAmount = availableAmount - _sendingFee;
                        }
                        else
                        {
                            _sendingAmount = 0;
                            Warning = Translations.CvInsufficientFunds;
                            IsAmountUpdating = false;
                            return;
                        }

                        var estimatedTxSize = await EstimateTxSizeAsync(_sendingAmount, _sendingFee);

                        if (estimatedTxSize == null || estimatedTxSize.Value == 0)
                        {
                            Warning = Translations.CvInsufficientFunds;
                            IsAmountUpdating = false;
                            return;
                        }

                        FeeRate = BtcBased.CoinToSatoshi(_sendingFee) / estimatedTxSize.Value;
                    }
                }
                finally
                {
                    IsAmountUpdating = false;
                }
            }

            else if (SelectedCurrency is Erc20Config)
            {
                try
                {
                    var availableAmount = SelectedCurrencyData.Balance;

                    if (availableAmount == 0)
                        return;

                    if (UseDefaultFee)
                    {
                        var (maxAmount, maxFeeAmount, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output, 0, 0,
                                false);

                        if (maxAmount > 0)
                            _sendingAmount = maxAmount;
                        else if (SelectedCurrencyData.Balance > 0)
                            Warning = string.Format(CultureInfo.InvariantCulture, Translations.CvInsufficientChainFunds,
                                SelectedCurrency.FeeCurrencyName);

                        _sendingFee = SelectedCurrency.GetDefaultFee();
                        _sendingFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();
                    }
                    else
                    {
                        if (_sendingFee < SelectedCurrency.GetDefaultFee() || _sendingFeePrice == 0)
                        {
                            Warning = Translations.CvLowFees;
                            if (_sendingFee == 0 || _sendingFeePrice == 0)
                            {
                                _sendingAmount = 0;
                                return;
                            }
                        }

                        var (maxAmount, maxFeeAmount, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output,
                                _sendingFee, _sendingFeePrice, false);

                        _sendingAmount = maxAmount;

                        if (maxAmount < availableAmount)
                            Warning = string.Format(CultureInfo.InvariantCulture, Translations.CvInsufficientChainFunds,
                                SelectedCurrency.FeeCurrencyName);

                        UpdateTotalFeeString(maxFeeAmount);
                    }
                }
                finally
                {
                    IsAmountUpdating = false;
                }
            }

            else if (SelectedCurrency is EthereumConfig)
            {
                try
                {
                    var availableAmount = SelectedCurrencyData.Balance;

                    if (availableAmount == 0)
                        return;

                    if (UseDefaultFee)
                    {
                        var (maxAmount, maxFeeAmount, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output, 0, 0,
                                false);

                        if (maxAmount > 0)
                            _sendingAmount = maxAmount;

                        _sendingFee = SelectedCurrency.GetDefaultFee();
                        _sendingFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();
                        UpdateTotalFeeString(maxFeeAmount);
                    }
                    else
                    {
                        if (_sendingFee < SelectedCurrency.GetDefaultFee() || _sendingFeePrice == 0)
                        {
                            Warning = Translations.CvLowFees;
                            if (_sendingFee == 0 || _sendingFeePrice == 0)
                            {
                                _sendingAmount = 0;
                                return;
                            }
                        }

                        var (maxAmount, maxFeeAmount, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output,
                                _sendingFee, _sendingFeePrice, false);

                        _sendingAmount = maxAmount;

                        if (maxAmount == 0 && availableAmount > 0)
                            Warning = Translations.CvInsufficientFunds;

                        UpdateTotalFeeString(maxFeeAmount);
                    }
                }
                finally
                {
                    IsAmountUpdating = false;
                }
            }

            else if (SelectedCurrency is Fa12Config)
            {
                try
                {
                    var availableAmount = SelectedCurrencyData.Balance;

                    if (availableAmount == 0)
                        return;

                    var defaultFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();

                    if (UseDefaultFee)
                    {
                        var (maxAmount, maxFeeAmount, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output, 0, 0,
                                true);

                        if (maxAmount > 0)
                            _sendingAmount = maxAmount;
                        else
                            Warning = string.Format(CultureInfo.InvariantCulture, Translations.CvInsufficientChainFunds,
                                SelectedCurrency.FeeCurrencyName);

                        _sendingFee = SelectedCurrency.GetFeeFromFeeAmount(maxFeeAmount, defaultFeePrice);
                    }
                    else
                    {
                        var (maxAmount, maxFee, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output, 0, 0,
                                false);

                        var feeAmount = SelectedCurrency.GetFeeAmount(_sendingFee, defaultFeePrice);

                        if (_sendingFee < maxFee)
                        {
                            Warning = Translations.CvLowFees;
                            if (_sendingFee == 0)
                            {
                                _sendingAmount = 0;
                                return;
                            }
                        }

                        _sendingAmount = maxAmount;

                        var (_, maxAvailableFee, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output,
                                decimal.MaxValue, 0, false);

                        if (maxAmount < availableAmount || feeAmount > maxAvailableFee)
                            Warning = string.Format(CultureInfo.InvariantCulture, Translations.CvInsufficientChainFunds,
                                SelectedCurrency.FeeCurrencyName);
                    }
                }
                finally
                {
                    IsAmountUpdating = false;
                }
            }

            else
            {
                try
                {
                    if (SelectedCurrencyData.Balance == 0)
                        return;

                    var defaultFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();

                    if (UseDefaultFee)
                    {
                        var (maxAmount, maxFeeAmount, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output, 0, 0,
                                true);

                        if (maxAmount > 0)
                            _sendingAmount = maxAmount;

                        _sendingFee = SelectedCurrency.GetFeeFromFeeAmount(maxFeeAmount, defaultFeePrice);
                    }
                    else
                    {
                        var (maxAmount, maxFeeAmount, _) = await account
                            .EstimateMaxAmountToSendAsync(SendingToAddress, BlockchainTransactionType.Output, 0, 0,
                                false);

                        var availableAmount = SelectedCurrency is BitcoinBasedConfig
                            ? SelectedCurrencyData.Balance
                            : maxAmount + maxFeeAmount;

                        var feeAmount = SelectedCurrency.GetFeeAmount(_sendingFee, defaultFeePrice);

                        if (availableAmount - feeAmount > 0)
                        {
                            _sendingAmount = availableAmount - feeAmount;

                            var estimatedFeeAmount = _sendingAmount != 0
                                ? await account.EstimateFeeAsync(SendingToAddress, _sendingAmount,
                                    BlockchainTransactionType.Output)
                                : 0;

                            if (estimatedFeeAmount == null || feeAmount < estimatedFeeAmount.Value)
                            {
                                Warning = Translations.CvLowFees;
                                if (_sendingFee == 0)
                                {
                                    _sendingAmount = 0;
                                    return;
                                }
                            }
                        }
                        else
                        {
                            _sendingAmount = 0;
                            Warning = Translations.CvInsufficientFunds;
                        }
                    }
                }
                finally
                {
                    IsAmountUpdating = false;
                }
            }
        }


        private async Task<int?> EstimateTxSizeAsync(
            decimal amount,
            decimal fee,
            CancellationToken cancellationToken = default)
        {
            return await accountStorage.Account
                .GetCurrencyAccount<BitcoinBasedAccount>(SelectedCurrency.Name)
                .EstimateTxSizeAsync(amount, fee, cancellationToken);
        }

        private void CheckForSimilarCurrencies()
        {
            if (SelectedCurrency.Name == SelectedSecondCurrency.Name ||
                accountStorage.Symbols.SymbolByCurrencies(SelectedCurrency, SelectedSecondCurrency) == null)
            {
                foreach (var availableCurrency in AvailableCurrencies)
                {
                    if (accountStorage.Symbols.SymbolByCurrencies(SelectedCurrency, availableCurrency) != null)
                    {
                        SelectedSecondCurrency = availableCurrency;
                        break;
                    }
                }
            }
        }

        public async void ResetSendData()
        {
            SendingToAddress = "";
            Warning = string.Empty;
            _sendingAmount = 0;
            _sendingFee = 0;
            _ethTotalFee = 0;
            _useDefaultFee = true;
            OpenedTx = null;

            CallCloseModals();

            if (_selectedCurrency is BitcoinBasedConfig)
            {
                _feeRate = await BtcBased.GetFeeRateAsync();
            }

            _sendingFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();
            CallUIRefresh();
        }

        public void SwapCurrencies()
        {
            var firstCurrency = SelectedCurrency;
            SelectedCurrency = SelectedSecondCurrency;
            SelectedSecondCurrency = firstCurrency;
        }

        private bool GetTezosTxIsReward(TezosTransaction tx)
        {
            if (bakerStorage.FromBakersList == null || bakerStorage.FromBakersList.Count() == 0 ||
                String.IsNullOrEmpty(tx.Alias))
            {
                return false;
            }

            return bakerStorage.FromBakersList.FindIndex(baker =>
                tx.Alias.ToLower().StartsWith($"{baker.Name.ToLower()} payouts")) != -1;
        }
    }
}