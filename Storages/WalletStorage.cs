using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Atomex;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet;
using Atomex.Abstract;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using atomex_frontend.atomex_data_structures;
using Atomex.MarketData.Abstract;
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
    private void CallMarketRefresh(bool force = true)
    {
      RefreshMarket?.Invoke(force);
    }

    private AccountStorage accountStorage;
    private BakerStorage bakerStorage;
    public List<Currency> AvailableCurrencies
    {
      get
      {
        try
        {
          return accountStorage.Account.Currencies.ToList();
        }
        catch (Exception e)
        {
          return new List<Currency>();
        }
      }
    }

    public Dictionary<string, CurrencyData> PortfolioData { get; set; } = new Dictionary<string, CurrencyData>();

    public Dictionary<string, Transaction> Transactions { get; set; } = new Dictionary<string, Transaction>();
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

    public decimal GetTotalDollars
    {
      get => Helper.SetPrecision(PortfolioData.Values.Sum(x => x.DollarValue), 1);
    }

    public CurrencyData SelectedCurrencyData
    {
      get => PortfolioData.TryGetValue(SelectedCurrency.Name, out CurrencyData data) ? data : new CurrencyData(accountStorage.Account.Currencies.Get<Currency>("BTC"), 0.0m, 0.0m, 0.0m);
    }

    private System.Timers.Timer debounceFirstCurrencySelection;
    private void OnAfterChangeFirstCurrency(Object source, ElapsedEventArgs e)
    {
      Task.Run(() =>
      {
        this.CheckForSimilarCurrencies();
        this.CallMarketRefresh();
      });
    }

    private Currency _selectedCurrency;
    public Currency SelectedCurrency
    {
      get => this._selectedCurrency;
      set
      {
        if (value.Name != this._selectedCurrency.Name)
        {
          this.ResetSendData();
        }

        this._selectedCurrency = value;

        if (CurrentWalletSection == WalletSection.Conversion)
        {
          debounceFirstCurrencySelection.Stop();
          debounceFirstCurrencySelection.Start();
        }
      }
    }

    private BitcoinBasedCurrency BtcBased => SelectedCurrency as BitcoinBasedCurrency;

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

    private Currency _selectedSecondCurrency;
    public Currency SelectedSecondCurrency
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
      get => GetEthreumBasedCurrency ? _ethTotalFee : this.SelectedCurrency.GetFeeAmount(_sendingFee, _sendingFeePrice);
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

    public string GasLimitString { get => Helper.DecimalToStr(this._sendingFee, "F0"); }

    public string GasPriceString { get => Helper.DecimalToStr(this._sendingFeePrice); }

    public decimal SendingAmountDollars
    {
      get => Helper.SetPrecision(this.GetDollarValue(this.SelectedCurrency.Name, this._sendingAmount), 2);
    }

    public bool GetEthreumBasedCurrency
    {
      get => this.SelectedCurrency is Ethereum || this.SelectedCurrency is Tether;
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
      set { this._isFeeUpdating = value; this.CallUIRefresh(); }
    }

    private bool _isAmountUpdating = false;
    private bool IsAmountUpdating
    {
      get => this._isAmountUpdating;
      set { this._isAmountUpdating = value; this.CallUIRefresh(); }
    }

    private bool _forceMarketUpdate = true;
    private WalletSection _currentWalletSection = WalletSection.Portfolio;
    public WalletSection CurrentWalletSection
    {
      get => _currentWalletSection;
      set
      {
        _currentWalletSection = value;

        if (_currentWalletSection == WalletSection.Conversion)
        {
          this.CheckForSimilarCurrencies();
          this.CallMarketRefresh(force: _forceMarketUpdate);
          _forceMarketUpdate = false;
        }

        if (_currentWalletSection == WalletSection.Wallets)
        {
          bakerStorage.LoadDelegationInfoAsync().FireAndForget();
        }
      }
    }

    public List<WalletAddressView> FromAddressList = new List<WalletAddressView>();
    public List<WalletAddress> xtzNotNullAddresses
    {
      get
      {
        var res = FromAddressList
            .Where(addr => addr.WalletAddress.Currency == "XTZ" && addr.AvailableBalance > 0)
            .Select(addr => addr.WalletAddress)
            .ToList();
        return res;
      }
    }

    public async void Initialize(bool IsRestarting)
    {
      if (accountStorage.AtomexApp != null)
      {
        if (accountStorage.AtomexApp.HasQuotesProvider && !IsRestarting)
        {
          accountStorage.AtomexApp.QuotesProvider.QuotesUpdated += async (object sender, EventArgs args) => await UpdatedQuotes();
        }
        accountStorage.AtomexApp.Account.BalanceUpdated += async (object sender, CurrencyEventArgs args) =>
        {
          if (args.Currency == "XTZ")
          {
            bakerStorage.LoadDelegationInfoAsync().FireAndForget();
          }
          await BalanceUpdatedHandler(args.Currency);
        };

        accountStorage.AtomexApp.CurrenciesProvider.Updated += (object sender, EventArgs args) =>
        {
          SelectedCurrency = this.accountStorage.Account.Currencies.Get<Currency>(SelectedCurrency.Name);
          _selectedSecondCurrency = this.accountStorage.Account.Currencies.Get<Currency>(SelectedSecondCurrency.Name);
        };

        accountStorage.AtomexApp.Account.UnconfirmedTransactionAdded += OnUnconfirmedTransactionAddedEventHandler;

        List<Currency> currenciesList = accountStorage.Account.Currencies.ToList();
        Transactions = new Dictionary<string, Transaction>();
        PortfolioData = new Dictionary<string, CurrencyData>();


        foreach (Currency currency in currenciesList)
        {
          CurrencyData initialCurrencyData = new CurrencyData(currency, 0, 0, 0.0m);
          PortfolioData.Add(currency.Name, initialCurrencyData);
          Console.WriteLine("Getting free address on initialize");
          GetFreeAddresses(initialCurrencyData.Currency);
        }

        await UpdatePortfolioAtStart();

        _selectedCurrency = this.accountStorage.Account.Currencies.Get<Currency>("BTC");
        _selectedSecondCurrency = this.accountStorage.Account.Currencies.Get<Currency>("XTZ");

        if (accountStorage.LoadingUpdate && !accountStorage.LoadFromRestore)
        {
          await ScanAllCurrencies();
        }

        URIHelper.NavigateTo("/wallet");
        accountStorage.WalletLoading = false;
        CurrentWalletSection = WalletSection.Portfolio;
        try
        {
          await jSRuntime.InvokeVoidAsync("walletLoaded");
        }
        catch { }
        if (accountStorage.LoadFromRestore)
        {
          await ScanAllCurrencies();
        }
      }
    }

    private void OnUnconfirmedTransactionAddedEventHandler(object sender, TransactionEventArgs e)
    {
      Console.WriteLine($"New Transaction on {e.Transaction.Currency.Name}, HANDLING with id  {e.Transaction.Id}");

      handleTransaction(e.Transaction);
      CallUIRefresh();
      //await jSRuntime.InvokeVoidAsync("showNotification", "You have new transaction", $"ID: {e.Transaction.Id}");
    }

    public decimal GetCurrencyData(Currency currency, string dataType)
    {
      if (dataType == "balance")
      {
        return PortfolioData.TryGetValue(currency.Name, out CurrencyData currencyData) ? currencyData.Balance : 0.0m;
      }
      if (dataType == "dollars")
      {
        return PortfolioData.TryGetValue(currency.Name, out CurrencyData currencyData) ? currencyData.DollarValue : 0.0m;
      }
      if (dataType == "percent")
      {
        return PortfolioData.TryGetValue(currency.Name, out CurrencyData currencyData) ? currencyData.Percent : 0.0m;
      }
      return 0.0m;
    }

    public async Task ScanCurrencyAsync(Currency currency, bool scanningAll = false)
    {
      if (!scanningAll)
      {
        IsUpdating = true;
      }
      try
      {
        await new HdWalletScanner(accountStorage.Account)
            .ScanAsync(currency.Name)
            .ConfigureAwait(false);
      }
      catch (Exception e)
      {
        Console.WriteLine($"Error update {currency.Name}: {e.ToString()}");
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
      foreach (var currency in accountStorage.Account.Currencies)
      {
        await CountCurrencyPortfolio(currency);
      }

      foreach (var currency in accountStorage.Account.Currencies)
      {
        RefreshCurrencyPercent(currency.Name);
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
      if (currency == null)
      {
        return;
      }

      await CountCurrencyPortfolio(currency);
      await RefreshTransactions(currency.Name);
      RefreshCurrencyAddresses(currency.Name);
      RefreshCurrencyPercent(currency.Name);

      this.CallUIRefresh();

      if (CurrentWalletSection == WalletSection.Portfolio)
      {
        await DrawDonutChart(updateData: true);
      }
    }

    private async Task CountCurrencyPortfolio(Currency currency)
    {
      Balance balance = (await accountStorage.Account.GetBalanceAsync(currency.Name));
      var availableBalance = balance.Available;

      if (!PortfolioData.TryGetValue(currency.Name, out CurrencyData currencyData))
      {
        PortfolioData.Add(currency.Name, new CurrencyData(currency, availableBalance, this.GetDollarValue(currency.Name, availableBalance), 0.0m));
      }
      else
      {
        currencyData.Balance = availableBalance;
        currencyData.DollarValue = this.GetDollarValue(currency.Name, availableBalance);
      }
    }

    private void RefreshCurrencyAddresses(string currencyName)
    {
      CurrencyData currData;
      if (PortfolioData.TryGetValue(currencyName, out currData))
      {
        GetFreeAddresses(currData.Currency);
      }
    }

    private void RefreshCurrencyPercent(string currencyName)
    {
      CurrencyData currData;

      if (PortfolioData.TryGetValue(currencyName, out currData))
      {
        currData.Percent = this.GetTotalDollars != 0 ? Helper.SetPrecision(currData.DollarValue / this.GetTotalDollars * 100.0m, 2) : 0;
      }
    }

    public async Task UpdatePortfolioAtStart()
    {
      List<Currency> currenciesList = accountStorage.Account.Currencies.ToList();

      foreach (Currency currency in currenciesList)
      {
        await CountCurrencyPortfolio(currency);
        await LoadTransactions(currency);
      }

      // FromAddressList = new List<WalletAddressView>();
      foreach (CurrencyData currencyData in PortfolioData.Values)
      {
        RefreshCurrencyPercent(currencyData.Currency.Name);
        RefreshCurrencyAddresses(currencyData.Currency.Name);
      }

      this.CallUIRefresh();

      if (CurrentWalletSection == WalletSection.Portfolio)
      {
        await DrawDonutChart(updateData: true);
      }
    }

    private void GetFreeAddresses(Currency Currency)
    {
      FromAddressList = FromAddressList
                          .Where(wa => wa.WalletAddress.Currency != Currency.Name)
                          .ToList(); // removing old addresses for this currency;

      if (Currency is BitcoinBasedCurrency || Currency is FA12 || Currency is ERC20)
      {
        var activeAddresses = accountStorage.Account
            .GetUnspentAddressesAsync(Currency.Name)
            .WaitForResult();

        var freeAddress = accountStorage.Account
            .GetFreeExternalAddressAsync(Currency.Name)
            .WaitForResult();

        var receiveAddresses = activeAddresses
            .Select(wa => new WalletAddressView(wa, Currency.Format))
            .ToList();

        if (activeAddresses.FirstOrDefault(w => w.Address == freeAddress.Address) == null)
          receiveAddresses.AddEx(new WalletAddressView(freeAddress, Currency.Format, isFreeAddress: true));

        FromAddressList.AddRange(receiveAddresses);
      }

      else if (Currency is Ethereum || Currency is Tezos)
      {
        var activeTokenAddresses = accountStorage.Account
            .GetUnspentTokenAddressesAsync(Currency.Name)
            .WaitForResult()
            .ToList();

        var activeAddresses = accountStorage.Account
            .GetUnspentAddressesAsync(Currency.Name)
            .WaitForResult()
            .ToList();

        activeTokenAddresses.ForEach(a => a.Balance = activeAddresses.Find(b => b.Address == a.Address)?.Balance ?? 0m);

        activeAddresses = activeAddresses.Where(a => activeTokenAddresses.FirstOrDefault(b => b.Address == a.Address) == null).ToList();

        var freeAddress = accountStorage.Account
            .GetFreeExternalAddressAsync(Currency.Name)
            .WaitForResult();

        var receiveAddresses = activeTokenAddresses.Select(w => new WalletAddressView(w, Currency.Format))
            .Concat(activeAddresses.Select(w => new WalletAddressView(w, Currency.Format)))
            .ToList();

        if (receiveAddresses.FirstOrDefault(w => w.Address == freeAddress.Address) == null)
          receiveAddresses.AddEx(new WalletAddressView(freeAddress, Currency.Format, isFreeAddress: true));

        receiveAddresses = receiveAddresses.Select(wa =>
        {
          wa.WalletAddress.Currency = Currency.Name;
          return wa;
        }).ToList();

        FromAddressList.AddRange(receiveAddresses);
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

      foreach (Currency currency in accountStorage.Account.Currencies)
      {
        if (PortfolioData.TryGetValue(currency.Name, out CurrencyData currencyData))
        {
          currenciesDollar.Add(currencyData.DollarValue);
          currenciesLabels.Add(currencyData.Currency.Description);
        }
        else
        {
          currenciesDollar.Add(0);
          currenciesLabels.Add(currency.Description);
        }
      }

      await jSRuntime.InvokeVoidAsync(updateData ? "updateChart" : "drawChart", currenciesDollar.ToArray(), currenciesLabels.ToArray(), GetTotalDollars);
    }

    public async Task RefreshTransactions(string currency)
    {
      var transactions = await accountStorage.Account.GetTransactionsAsync(currency);
      foreach (var tx in transactions)
      {
        var txAge = DateTime.Now - tx.CreationTime;
        if (Transactions.ContainsKey($"{tx.Id}/{tx.Currency.Name}") && tx.State == BlockchainTransactionState.Confirmed && txAge.Value.TotalDays > 1)
        {
          continue;
        }
        else
        {
          handleTransaction(tx);
        }
      }
    }

    public async Task LoadTransactions(Currency currency)
    {
      var transactions = await accountStorage.Account.GetTransactionsAsync(currency.Name);
      foreach (var tx in transactions)
      {
        handleTransaction(tx);
      }
    }


    private void AddTransaction(Transaction tx, Currency currency)
    {
      Transaction oldTx;
      string TxKeyInDict = $"{tx.Id}/{tx.Currency.Name}";
      var type = tx.Type.HasFlag(BlockchainTransactionType.Input) || tx.Type.HasFlag(BlockchainTransactionType.SwapRedeem) ? "income" : tx.Type.HasFlag(BlockchainTransactionType.Output) ? "outcome" : "";
      if (Transactions.TryGetValue(TxKeyInDict, out oldTx))
      {
        Transactions[TxKeyInDict] = tx;
        if (oldTx.State != BlockchainTransactionState.Confirmed && tx.State == BlockchainTransactionState.Confirmed && tx.Amount != 0 && !AllPortfolioUpdating && !IsUpdating)
        {
          jSRuntime.InvokeVoidAsync("showNotification", $"{tx.Currency.Description} {type}", tx.Description, $"/css/images/{tx.Currency.Description.ToLower()}_90x90.png");
        }
      }
      else
      {
        Transactions.Add(TxKeyInDict, tx);
        if (tx.State == BlockchainTransactionState.Confirmed && tx.Amount != 0 && !AllPortfolioUpdating && !IsUpdating)
        {
          jSRuntime.InvokeVoidAsync("showNotification", $"{tx.Currency.Description} {type}", tx.Description, $"/css/images/{tx.Currency.Description.ToLower()}_90x90.png");
        }
      }
    }

    private void handleTransaction(IBlockchainTransaction tx)
    {
      decimal amount = 0;
      string description = "";
      switch (tx.Currency)
      {
        case BitcoinBasedCurrency _:
          IBitcoinBasedTransaction btcBasedTrans = (IBitcoinBasedTransaction)tx;
          amount = CurrHelper.GetTransAmount(btcBasedTrans);
          description = CurrHelper.GetTransDescription(tx, amount, CurrHelper.GetFee(btcBasedTrans));
          var BtcFee = btcBasedTrans.Fees != null
            ? btcBasedTrans.Fees.Value / (decimal)tx.Currency.DigitsMultiplier
            : 0;

          AddTransaction(
            new Transaction(
              tx.Currency,
              btcBasedTrans.Id,
              btcBasedTrans.State,
              btcBasedTrans.Type,
              btcBasedTrans.CreationTime,
              btcBasedTrans.IsConfirmed,
              amount,
              description,
              BtcFee
            ), tx.Currency);
          break;
        case TBTC _:
        case WBTC _:
        case Tether _:
          EthereumTransaction usdtTrans = (EthereumTransaction)tx;
          amount = CurrHelper.GetTransAmount(usdtTrans);
          description = CurrHelper.GetTransDescription(tx, amount, 0);
          string FromUsdt = usdtTrans.From;
          string ToUsdt = usdtTrans.To;
          decimal GasPriceUsdt = Ethereum.WeiToGwei((decimal)usdtTrans.GasPrice);
          decimal GasLimitUsdt = (decimal)usdtTrans.GasLimit;
          decimal GasUsedUsdt = (decimal)usdtTrans.GasUsed;
          bool IsInternalUsdt = usdtTrans.IsInternal;

          AddTransaction(
            new Transaction(
              tx.Currency,
              usdtTrans.Id,
              usdtTrans.State,
              usdtTrans.Type,
              usdtTrans.CreationTime,
              usdtTrans.IsConfirmed,
              amount,
              description,
              from: FromUsdt,
              to: ToUsdt,
              gasPrice: GasPriceUsdt,
              gasLimit: GasLimitUsdt,
              gasUsed: GasUsedUsdt,
              isInternal: IsInternalUsdt
            ), tx.Currency);
          break;
        case Ethereum _:
          EthereumTransaction ethTrans = (EthereumTransaction)tx;
          amount = CurrHelper.GetTransAmount(ethTrans);
          description = CurrHelper.GetTransDescription(ethTrans, amount, CurrHelper.GetFee(ethTrans));
          string FromEth = ethTrans.From;
          string ToEth = ethTrans.To;
          decimal GasPriceEth = Ethereum.WeiToGwei((decimal)ethTrans.GasPrice);
          decimal GasLimitEth = (decimal)ethTrans.GasLimit;
          decimal GasUsedEth = (decimal)ethTrans.GasUsed;

          decimal FeeEth = Ethereum.WeiToEth(ethTrans.GasUsed * ethTrans.GasPrice);

          bool IsInternalEth = ethTrans.IsInternal;

          AddTransaction(
            new Transaction(
              tx.Currency,
              ethTrans.Id,
              ethTrans.State,
              ethTrans.Type,
              ethTrans.CreationTime,
              ethTrans.IsConfirmed,
              amount,
              description,
              fee: FeeEth,
              from: FromEth,
              to: ToEth,
              gasPrice: GasPriceEth,
              gasLimit: GasLimitEth,
              gasUsed: GasUsedEth,
              isInternal: IsInternalEth
            ), tx.Currency);
          break;

        case FA12 _:
          TezosTransaction fa12Trans = (TezosTransaction)tx;

          amount = CurrHelper.GetTransAmount(fa12Trans);
          description = CurrHelper.GetTransDescription(fa12Trans, amount, 0);
          string FromFa12 = fa12Trans.From;
          string ToFa12 = fa12Trans.To;
          decimal GasLimitFa12 = fa12Trans.GasLimit;
          decimal FeeFa12 = Tezos.MtzToTz(fa12Trans.Fee);
          bool IsInternalFa12 = fa12Trans.IsInternal;

          AddTransaction(
            new Transaction(
              tx.Currency,
              fa12Trans.Id,
              fa12Trans.State,
              fa12Trans.Type,
              fa12Trans.CreationTime,
              fa12Trans.IsConfirmed,
              amount,
              description,
              fa12Trans.Fee,
              from: FromFa12,
              to: ToFa12,
              gasLimit: GasLimitFa12,
              isInternal: IsInternalFa12,
              alias: fa12Trans.Alias
            ), tx.Currency);
          break;

        case Tezos _:
          TezosTransaction xtzTrans = (TezosTransaction)tx;

          amount = CurrHelper.GetTransAmount(xtzTrans);
          decimal FeeXtz = CurrHelper.GetFee(xtzTrans);
          description = CurrHelper.GetTransDescription(xtzTrans, amount, FeeXtz);
          string FromXtz = xtzTrans.From;
          string ToXtz = xtzTrans.To;
          decimal GasLimitXtz = xtzTrans.GasLimit;
          bool IsInternalXtz = xtzTrans.IsInternal;

          AddTransaction(
            new Transaction(
              tx.Currency,
              xtzTrans.Id,
              xtzTrans.State,
              xtzTrans.Type,
              xtzTrans.CreationTime,
              xtzTrans.IsConfirmed,
              amount,
              description,
              xtzTrans.Fee,
              from: FromXtz,
              to: ToXtz,
              gasLimit: GasLimitXtz,
              isInternal: IsInternalXtz,
              alias: xtzTrans.Alias
            ), tx.Currency);
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


      if ((SelectedCurrency is FA12 || SelectedCurrency is Tezos) && !SelectedCurrency.IsValidAddress(SendingToAddress))
      {
        CallUIRefresh();
        return;
      }

      Warning = string.Empty;
      var previousAmount = _sendingAmount;
      _sendingAmount = amount;

      // this.CallUIRefresh();
      IsAmountUpdating = true;

      if (SelectedCurrency is BitcoinBasedCurrency)
      {
        try
        {
          if (UseDefaultFee)
          {
            var (maxAmount, _, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output);

            if (_sendingAmount > maxAmount)
            {
              Warning = Translations.CvInsufficientFunds;
              IsAmountUpdating = false;
              return;
            }

            var estimatedFeeAmount = _sendingAmount != 0
                ? await accountStorage.Account.EstimateFeeAsync(SelectedCurrency.Name, SendingToAddress, _sendingAmount, BlockchainTransactionType.Output)
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
      else

      if (SelectedCurrency is Ethereum)
      {
        try
        {
          if (UseDefaultFee)
          {
            var (maxAmount, maxFeeAmount, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, 0, 0, false);

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
            var (maxAmount, maxFeeAmount, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, _sendingFee, _sendingFeePrice, false);

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
      else

      if (SelectedCurrency is ERC20)
      {
        try
        {
          var availableAmount = SelectedCurrencyData.Balance;

          if (UseDefaultFee)
          {
            var (maxAmount, maxFeeAmount, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, 0, 0, false);

            _sendingFee = SelectedCurrency.GetDefaultFee();

            _sendingFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();

            if (_sendingAmount > maxAmount)
            {
              if (_sendingAmount <= availableAmount)
                Warning = string.Format(CultureInfo.InvariantCulture, Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);
              else
                Warning = Translations.CvInsufficientFunds;

              return;
            }

            UpdateTotalFeeString();
          }
          else
          {
            var (maxAmount, _, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, _sendingFee, _sendingFeePrice, false);

            if (_sendingAmount > maxAmount)
            {
              if (_sendingAmount <= availableAmount)
                Warning = string.Format(CultureInfo.InvariantCulture, Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);
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

      else if (SelectedCurrency is FA12)
      {
        try
        {
          var availableAmount = SelectedCurrencyData.Balance;
          if (UseDefaultFee)
          {
            var (maxAmount, maxFeeAmount, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, 0, 0, false);

            if (_sendingAmount > maxAmount)
            {
              if (_sendingAmount <= availableAmount)
                Warning = string.Format(CultureInfo.InvariantCulture, Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);
              else
                Warning = Translations.CvInsufficientFunds;

              return;
            }

            var estimatedFeeAmount = _sendingAmount != 0
                ? await accountStorage.Account.EstimateFeeAsync(SelectedCurrency.Name, SendingToAddress, _sendingAmount, BlockchainTransactionType.Output)
                : 0;

            var defaultFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();
            _sendingFee = SelectedCurrency.GetFeeFromFeeAmount(estimatedFeeAmount ?? SelectedCurrency.GetDefaultFee(), defaultFeePrice);
          }
          else
          {
            var (maxAmount, _, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, 0, 0, false);

            if (_sendingAmount > maxAmount)
            {
              if (_sendingAmount <= availableAmount)
                Warning = string.Format(CultureInfo.InvariantCulture, Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);
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
            var (maxAmount, maxFeeAmount, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, 0, 0, true);

            if (_sendingAmount > maxAmount)
            {
              Warning = Translations.CvInsufficientFunds;
              return;
            }

            var estimatedFeeAmount = _sendingAmount != 0
                ? await accountStorage.Account.EstimateFeeAsync(SelectedCurrency.Name, SendingToAddress, _sendingAmount, BlockchainTransactionType.Output)
                : 0;

            _sendingFee = SelectedCurrency.GetFeeFromFeeAmount(estimatedFeeAmount ?? SelectedCurrency.GetDefaultFee(), defaultFeePrice);
          }
          else
          {
            var (maxAmount, maxFeeAmount, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, 0, 0, false);

            var availableAmount = SelectedCurrency is BitcoinBasedCurrency
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
        var feeAmount = totalFeeAmount > 0
            ? totalFeeAmount
            : SelectedCurrency.GetFeeAmount(_sendingFee, _sendingFeePrice) > 0
                ? await accountStorage.Account.EstimateFeeAsync(SelectedCurrency.Name, SendingToAddress, _sendingAmount, BlockchainTransactionType.Output, _sendingFee, _sendingFeePrice)
                : 0;

        if (feeAmount != null)
        {
          _ethTotalFee = feeAmount.Value;
        }
      }
      catch { }
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
        // _sendingFee = 0;
        this.CallUIRefresh();
        return;
      }

      IsFeeUpdating = true;

      _sendingFee = Math.Min(fee, SelectedCurrency.GetMaximumFee());
      Warning = string.Empty;

      if (SelectedCurrency is BitcoinBasedCurrency)
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

      else if (SelectedCurrency is ERC20)
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
            var (maxAmount, maxFee, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, _sendingFee, _sendingFeePrice, false);

            if (_sendingAmount > maxAmount)
            {
              var availableAmount = SelectedCurrencyData.Balance;

              if (_sendingAmount <= availableAmount)
                Warning = string.Format(CultureInfo.InvariantCulture, Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);
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

      else if (SelectedCurrency is Ethereum)
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
            var (maxAmount, maxFee, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, _sendingFee, _sendingFeePrice, false);

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

      else if (SelectedCurrency is FA12)
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

            var (maxAmount, maxAvailableFee, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, decimal.MaxValue, 0, false);

            var defaultFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();
            var feeAmount = SelectedCurrency.GetFeeAmount(_sendingFee, defaultFeePrice);

            var estimatedFeeAmount = _sendingAmount != 0
                ? await accountStorage.Account.EstimateFeeAsync(SelectedCurrency.Name, SendingToAddress, _sendingAmount, BlockchainTransactionType.Output)
                : 0;

            if (_sendingAmount > maxAmount)
            {
              if (_sendingAmount <= availableAmount)
                Warning = string.Format(CultureInfo.InvariantCulture, Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);
              else
                Warning = Translations.CvInsufficientFunds;

              return;
            }
            else if (estimatedFeeAmount == null || feeAmount < estimatedFeeAmount.Value)
            {
              Warning = Translations.CvLowFees;
            }

            if (feeAmount > maxAvailableFee)
              Warning = string.Format(CultureInfo.InvariantCulture, Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);
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
                ? await accountStorage.Account.EstimateFeeAsync(SelectedCurrency.Name, SendingToAddress, _sendingAmount, BlockchainTransactionType.Output)
                : 0;

            var (maxAmount, maxFeeAmount, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, 0, 0, false);

            var availableAmount = SelectedCurrency is BitcoinBasedCurrency
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

      IsFeeUpdating = true;

      _sendingFeePrice = value;

      Warning = string.Empty;

      if (SelectedCurrency is Ethereum)
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
            var (maxAmount, maxFee, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, _sendingFee, _sendingFeePrice, false);

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

      else if (SelectedCurrency is ERC20)
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
            var (maxAmount, maxFee, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, _sendingFee, _sendingFeePrice, false);

            if (_sendingAmount > maxAmount)
            {
              var availableAmount = SelectedCurrencyData.Balance;

              if (_sendingAmount <= availableAmount)
                Warning = string.Format(CultureInfo.InvariantCulture, Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);
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

      var feeAmount = !isToken ? SelectedCurrency is Ethereum ? SelectedCurrency.GetFeeAmount(SendingFee, SendingFeePrice) : SendingFee : 0;

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

      IsAmountUpdating = true;

      Warning = string.Empty;

      if (SelectedCurrency is BitcoinBasedCurrency)
      {
        try
        {
          if (SelectedCurrencyData.Balance == 0)
            return;

          if (UseDefaultFee) // auto fee
          {
            var (maxAmount, maxFeeAmount, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output);

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

      else if (SelectedCurrency is ERC20)
      {
        try
        {
          var availableAmount = SelectedCurrencyData.Balance;

          if (availableAmount == 0)
            return;

          if (UseDefaultFee)
          {
            var (maxAmount, maxFeeAmount, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, 0, 0, false);

            if (maxAmount > 0)
              _sendingAmount = maxAmount;
            else if (SelectedCurrencyData.Balance > 0)
              Warning = string.Format(CultureInfo.InvariantCulture, Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);

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

            var (maxAmount, maxFeeAmount, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, _sendingFee, _sendingFeePrice, false);

            _sendingAmount = maxAmount;

            if (maxAmount < availableAmount)
              Warning = string.Format(CultureInfo.InvariantCulture, Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);

            UpdateTotalFeeString(maxFeeAmount);
          }
        }
        finally
        {
          IsAmountUpdating = false;
        }
      }

      else if (SelectedCurrency is Ethereum)
      {
        try
        {
          var availableAmount = SelectedCurrencyData.Balance;

          if (availableAmount == 0)
            return;

          if (UseDefaultFee)
          {
            var (maxAmount, maxFeeAmount, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, 0, 0, false);

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

            var (maxAmount, maxFeeAmount, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, _sendingFee, _sendingFeePrice, false);

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

      else if (SelectedCurrency is FA12)
      {
        try
        {
          var availableAmount = SelectedCurrencyData.Balance;

          if (availableAmount == 0)
            return;

          var defaultFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();

          if (UseDefaultFee)
          {
            var (maxAmount, maxFeeAmount, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, 0, 0, true);

            if (maxAmount > 0)
              _sendingAmount = maxAmount;
            else
              Warning = string.Format(CultureInfo.InvariantCulture, Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);

            _sendingFee = SelectedCurrency.GetFeeFromFeeAmount(maxFeeAmount, defaultFeePrice);
          }
          else
          {
            var (maxAmount, maxFee, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, 0, 0, false);

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

            var (_, maxAvailableFee, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, decimal.MaxValue, 0, false);

            if (maxAmount < availableAmount || feeAmount > maxAvailableFee)
              Warning = string.Format(CultureInfo.InvariantCulture, Translations.CvInsufficientChainFunds, SelectedCurrency.FeeCurrencyName);
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
            var (maxAmount, maxFeeAmount, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, 0, 0, true);

            if (maxAmount > 0)
              _sendingAmount = maxAmount;

            _sendingFee = SelectedCurrency.GetFeeFromFeeAmount(maxFeeAmount, defaultFeePrice);
          }
          else
          {
            var (maxAmount, maxFeeAmount, _) = await accountStorage.Account
                .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, 0, 0, false);

            var availableAmount = SelectedCurrency is BitcoinBasedCurrency
                ? SelectedCurrencyData.Balance
                : maxAmount + maxFeeAmount;

            var feeAmount = SelectedCurrency.GetFeeAmount(_sendingFee, defaultFeePrice);

            if (availableAmount - feeAmount > 0)
            {
              _sendingAmount = availableAmount - feeAmount;

              var estimatedFeeAmount = _sendingAmount != 0
                  ? await accountStorage.Account.EstimateFeeAsync(SelectedCurrency.Name, SendingToAddress, _sendingAmount, BlockchainTransactionType.Output)
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
      if (SelectedCurrency.Name == SelectedSecondCurrency.Name || accountStorage.AtomexApp.Account.Symbols.SymbolByCurrencies(SelectedCurrency, SelectedSecondCurrency) == null)
      {
        foreach (Currency availableCurrency in AvailableCurrencies)
        {
          if (accountStorage.AtomexApp.Account.Symbols.SymbolByCurrencies(SelectedCurrency, availableCurrency) != null)
          {
            SelectedSecondCurrency = availableCurrency;
            break;
          }
        }
      }
    }

    public async void ResetSendData()
    {
      this.SendingToAddress = "";
      this.Warning = string.Empty;
      this._sendingAmount = 0;
      this._sendingFee = 0;
      this._ethTotalFee = 0;
      this._useDefaultFee = true;

      this.CallCloseModals();

      if (_selectedCurrency is BitcoinBasedCurrency)
      {
        this._feeRate = await BtcBased.GetFeeRateAsync();
      }
      this._sendingFeePrice = await SelectedCurrency.GetDefaultFeePriceAsync();
      this.CallUIRefresh();
    }

    public void SwapCurrencies()
    {
      var firstCurrency = SelectedCurrency;
      SelectedCurrency = SelectedSecondCurrency;
      SelectedSecondCurrency = firstCurrency;
    }
  }
}