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
using Atomex.TezosTokens;
using atomex_frontend.Common;
using Microsoft.AspNetCore.Components;
using System.Timers;

using LiteDB;
using Atomex.Common.Bson;

namespace atomex_frontend.Storages
{

  public class WalletStorage
  {
    public WalletStorage(AccountStorage accountStorage, IJSRuntime JSRuntime, NavigationManager uriHelper)
    {
      this.accountStorage = accountStorage;
      this.jSRuntime = JSRuntime;
      this.URIHelper = uriHelper;

      this.accountStorage.InitializeCallback += Initialize;

      debounceFirstCurrencySelection = new System.Timers.Timer(1);
      debounceFirstCurrencySelection.Elapsed += OnAfterChangeFirstCurrency;
      debounceFirstCurrencySelection.AutoReset = false;

      debounceSecondCurrencySelection = new System.Timers.Timer(1);
      debounceSecondCurrencySelection.Elapsed += OnAfterChangeSecondCurrency;
      debounceSecondCurrencySelection.AutoReset = false;
    }

    private IJSRuntime jSRuntime;
    private NavigationManager URIHelper;
    public event Action RefreshRequested;
    private void CallUIRefresh()
    {
      RefreshRequested?.Invoke();
    }

    public event Action<bool> RefreshMarket;
    private void CallMarketRefresh(bool force = true)
    {
      RefreshMarket?.Invoke(force);
    }

    private AccountStorage accountStorage;
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

    public Dictionary<Currency, CurrencyData> PortfolioData { get; set; } = new Dictionary<Currency, CurrencyData>();
    public List<Transaction> Transactions { get; set; } = new List<Transaction>();
    public List<Transaction> SelectedCurrencyTransactions
    {
      get => this.Transactions
        .FindAll(tx => tx.Currency == SelectedCurrency)
        .OrderByDescending(a => a.CreationTime)
        .ToList();
    }

    public decimal GetTotalDollars
    {
      get => Helper.SetPrecision(PortfolioData.Values.Sum(x => x.DollarValue), 1);
    }

    public CurrencyData SelectedCurrencyData
    {
      get => PortfolioData.TryGetValue(SelectedCurrency, out CurrencyData data) ? data : new CurrencyData(AccountStorage.Bitcoin, 0.0m, 0.0m, 0.0m);
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

    private Currency _selectedCurrency = AccountStorage.Bitcoin;
    public Currency SelectedCurrency
    {
      get => this._selectedCurrency;
      set
      {
        if (this._selectedCurrency.Name != value.Name)
        {
          this._selectedCurrency = value;

          if (CurrentWalletSection == WalletSection.Conversion)
          {
            debounceFirstCurrencySelection.Stop();
            debounceFirstCurrencySelection.Start();
          }

          this.ResetSendData();
        }
      }
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

    private Currency _selectedSecondCurrency = AccountStorage.Tezos;
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
    }

    public decimal TotalFee
    {
      get => this.SelectedCurrency.GetFeeAmount(_sendingFee, _sendingFeePrice);
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
      get => Helper.SetPrecision(this.GetDollarValue(this.SelectedCurrency, this._sendingAmount), 2);
    }

    public bool GetEthreumBasedCurrency
    {
      get => this.SelectedCurrency == AccountStorage.Ethereum || this.SelectedCurrency == AccountStorage.Tether;
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
      }
    }


    public async void Initialize(bool IsRestarting)
    {
      if (accountStorage.AtomexApp != null)
      {
        if (accountStorage.AtomexApp.HasQuotesProvider && !IsRestarting)
        {
          accountStorage.AtomexApp.QuotesProvider.QuotesUpdated += async (object sender, EventArgs args) => await UpdatePortfolioAsync();
        }
        accountStorage.AtomexApp.Account.BalanceUpdated += async (object sender, CurrencyEventArgs args) => await UpdatePortfolioAsync();
        accountStorage.AtomexApp.Account.UnconfirmedTransactionAdded += OnUnconfirmedTransactionAddedEventHandler;

        List<Currency> currenciesList = accountStorage.Account.Currencies.ToList();
        Transactions = new List<Transaction>();
        PortfolioData = new Dictionary<Currency, CurrencyData>();

        foreach (Currency currency in currenciesList)
        {
          CurrencyData initialCurrencyData = new CurrencyData(currency, 0, 0, 0.0m);
          PortfolioData.Add(currency, initialCurrencyData);
          Console.WriteLine("Getting free address on initialize");
          initialCurrencyData.FreeExternalAddress = (await this.accountStorage.Account.GetFreeExternalAddressAsync(initialCurrencyData.Currency.Name)).Address;

        }

        CreateBsonMapper();
        await UpdatePortfolioAsync();
        URIHelper.NavigateTo("/wallet");
        accountStorage.WalletLoading = false;
      }
    }

    private async void OnUnconfirmedTransactionAddedEventHandler(object sender, TransactionEventArgs e)
    {
      Console.WriteLine($"New Transaction on {e.Transaction.Currency.Name}, HANDLING with id  {e.Transaction.Id}");
      handleTransaction(e.Transaction);
      await jSRuntime.InvokeVoidAsync("showNotification", "You have new transaction", $"ID: {e.Transaction.Id}");
    }

    public decimal GetCurrencyData(Currency currency, string dataType)
    {
      if (dataType == "balance")
      {
        return PortfolioData.TryGetValue(currency, out CurrencyData currencyData) ? currencyData.Balance : 0.0m;
      }
      if (dataType == "dollars")
      {
        return PortfolioData.TryGetValue(currency, out CurrencyData currencyData) ? currencyData.DollarValue : 0.0m;
      }
      if (dataType == "percent")
      {
        return PortfolioData.TryGetValue(currency, out CurrencyData currencyData) ? currencyData.Percent : 0.0m;
      }
      return 0.0m;
    }

    public async Task ScanCurrencyAsync(Currency currency)
    {
      IsUpdating = true;
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
        IsUpdating = false;
      }

    }

    // public async Task<WalletAddress> GetFreeAddress()
    // {
    //   return await accountStorage.Account.GetFreeExternalAddressAsync(this.SelectedCurrency.Name);
    // }

    public async Task UpdatePortfolioAsync()
    {
      List<Currency> currenciesList = accountStorage.Account.Currencies.ToList();

      foreach (Currency currency in currenciesList)
      {
        Balance balance = (await accountStorage.Account.GetBalanceAsync(currency.Name));
        var availableBalance = balance.Available;
        if (!PortfolioData.TryGetValue(currency, out CurrencyData currencyData))
        {
          PortfolioData.Add(currency, new CurrencyData(currency, availableBalance, this.GetDollarValue(currency, availableBalance), 0.0m));
        }
        else
        {
          currencyData.Balance = availableBalance;
          currencyData.DollarValue = this.GetDollarValue(currency, availableBalance);
        }

        await GetTransactions(currency);
      }

      foreach (CurrencyData currencyData in PortfolioData.Values)
      {
        currencyData.Percent = this.GetTotalDollars != 0 ? Helper.SetPrecision(currencyData.DollarValue / this.GetTotalDollars * 100.0m, 2) : 0;
        currencyData.FreeExternalAddress = (await this.accountStorage.Account.GetFreeExternalAddressAsync(currencyData.Currency.Name)).Address;
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

      foreach (Currency currency in accountStorage.Account.Currencies)
      {
        if (PortfolioData.TryGetValue(currency, out CurrencyData currencyData))
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

    public async Task GetTransactions(Currency currency)
    {
      var transactions = await accountStorage.Account.GetTransactionsAsync(currency.Name);
      foreach (var tx in transactions)
      {
        handleTransaction(tx);
      }
    }

    private void AddTransaction(Transaction tx, Currency currency)
    {
      var oldTransIndex = Transactions.FindIndex(oldTx => oldTx.Id == tx.Id);
      if (oldTransIndex == -1)
      {
        Transactions.Add(tx);
      }
      else
      {
        Transactions[oldTransIndex] = tx;
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
          description = CurrHelper.GetTransDescription(tx, amount);
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
        case Tether _:
          EthereumTransaction usdtTrans = (EthereumTransaction)tx;
          amount = CurrHelper.GetTransAmount(usdtTrans);
          description = CurrHelper.GetTransDescription(tx, amount);
          string FromUsdt = usdtTrans.From;
          string ToUsdt = usdtTrans.To;
          decimal GasPriceUsdt = (decimal)usdtTrans.GasPrice;
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
          description = CurrHelper.GetTransDescription(ethTrans, amount);
          string FromEth = ethTrans.From;
          string ToEth = ethTrans.To;
          decimal GasPriceEth = (decimal)ethTrans.GasPrice;
          decimal GasLimitEth = (decimal)ethTrans.GasLimit;
          decimal GasUsedEth = (decimal)ethTrans.GasUsed;
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
              from: FromEth,
              to: ToEth,
              gasPrice: GasPriceEth,
              gasLimit: GasLimitEth,
              gasUsed: GasUsedEth,
              isInternal: IsInternalEth
            ), tx.Currency);
          break;

        case FA12 _:
        case Tezos _:
          TezosTransaction xtzTrans = (TezosTransaction)tx;
          amount = CurrHelper.GetTransAmount(xtzTrans);
          description = CurrHelper.GetTransDescription(xtzTrans, amount);
          string FromXtz = xtzTrans.From;
          string ToXtz = xtzTrans.To;
          decimal GasLimitXtz = xtzTrans.GasLimit;
          decimal FeeXtz = xtzTrans.Fee;
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
              isInternal: IsInternalXtz
            ), tx.Currency);
          break;
      }
    }

    public decimal GetDollarValue(Currency currency, decimal amount)
    {
      if (accountStorage.QuotesProvider != null)
      {
        return Helper.SetPrecision(accountStorage.QuotesProvider.GetQuote(currency.Name, "USD").Bid * amount, 2);
      }
      return 0;
    }

    protected async void UpdateSendingAmount(decimal amount)
    {
      bool TetherOrFa12 = SelectedCurrency == AccountStorage.FA12 || SelectedCurrency == AccountStorage.Tether;

      if ((SelectedCurrency == AccountStorage.FA12 || SelectedCurrency == AccountStorage.Tezos) && !SelectedCurrency.IsValidAddress(SendingToAddress))
      {
        this.ResetSendData();
        return;
      }
      var previousAmount = _sendingAmount;

      _sendingAmount = amount;
      this.CallUIRefresh();

      if (UseDefaultFee)
      {
        var (maxAmount, maxFeeAmount, _) = await accountStorage.Account
            .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, true);

        var availableAmount = SelectedCurrency is BitcoinBasedCurrency
          ? SelectedCurrencyData.Balance
          : !TetherOrFa12 ? maxAmount + maxFeeAmount : maxAmount;

        var estimatedFeeAmount = _sendingAmount != 0
            ? (_sendingAmount < availableAmount
                ? await accountStorage.Account.EstimateFeeAsync(SelectedCurrency.Name, SendingToAddress, _sendingAmount, BlockchainTransactionType.Output)
                : null)
            : 0;

        if (estimatedFeeAmount == null)
        {
          if (maxAmount > 0)
          {
            _sendingAmount = maxAmount;
            estimatedFeeAmount = maxFeeAmount;
          }
          else
          {
            _sendingAmount = previousAmount;
            return;
          }
        }

        if (!TetherOrFa12)
        {
          if (_sendingAmount + estimatedFeeAmount.Value > availableAmount)
            _sendingAmount = Math.Max(availableAmount - estimatedFeeAmount.Value, 0);
        }
        else
        {
          if (_sendingAmount > availableAmount)
            _sendingAmount = Math.Max(availableAmount, 0);
        }

        if (_sendingAmount == 0)
          estimatedFeeAmount = 0;

        _sendingFee = SelectedCurrency.GetFeeFromFeeAmount(estimatedFeeAmount.Value, SelectedCurrency.GetDefaultFeePrice());
        _sendingFeePrice = SelectedCurrency.GetDefaultFeePrice();
      }
      else
      {
        var (maxAmount, maxFeeAmount, _) = await accountStorage.Account
            .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, false);

        var availableAmount = SelectedCurrency is BitcoinBasedCurrency
            ? SelectedCurrencyData.Balance
            : !TetherOrFa12 ? maxAmount + maxFeeAmount : maxAmount;

        var feeAmount = Math.Max(SelectedCurrency.GetFeeAmount(_sendingFee, this._sendingFeePrice), maxFeeAmount);

        if (!TetherOrFa12)
        {
          if (_sendingAmount + feeAmount > availableAmount)
          {
            _sendingAmount = Math.Max(availableAmount - feeAmount, 0);
          }
        }
        else
        {
          if (_sendingAmount > availableAmount)
            _sendingAmount = Math.Max(availableAmount, 0);
        }

        if (_sendingFee != 0)
          SendingFee = _sendingFee;

      }
      this.CallUIRefresh();
    }

    protected async void UpdateSendingFee(decimal fee)
    {
      this._sendingFee = fee;
      this.CallUIRefresh();
      if (_sendingAmount == 0)
      {
        _sendingFee = 0;
        this.CallUIRefresh();
        return;
      }

      _sendingFee = Math.Min(fee, SelectedCurrency.GetMaximumFee());

      if (!UseDefaultFee)
      {
        var estimatedFeeAmount = _sendingAmount != 0
            ? await accountStorage.Account.EstimateFeeAsync(SelectedCurrency.Name, SendingToAddress, _sendingAmount, BlockchainTransactionType.Output)
            : 0;

        var feeAmount = this.GetEthreumBasedCurrency
            ? SelectedCurrency.GetFeeAmount(_sendingFee, _sendingFeePrice)
            : _sendingFee;

        if ((feeAmount > estimatedFeeAmount.Value) && SelectedCurrency != AccountStorage.Tether && SelectedCurrency != AccountStorage.FA12)
        {
          var (maxAmount, maxFee, _) = await accountStorage.Account
              .EstimateMaxAmountToSendAsync(SelectedCurrency.Name, SendingToAddress, BlockchainTransactionType.Output, true);

          var availableAmount = SelectedCurrency is BitcoinBasedCurrency
              ? SelectedCurrencyData.Balance
              : maxAmount + maxFee;

          if (_sendingAmount + feeAmount > availableAmount)
          {
            _sendingAmount = Math.Max(availableAmount - feeAmount, 0);
          }
        }
        else if (feeAmount < estimatedFeeAmount.Value)

          _sendingFee = this.GetEthreumBasedCurrency
            ? SelectedCurrency.GetFeeFromFeeAmount(estimatedFeeAmount.Value, SelectedCurrency.GetDefaultFeePrice())
            : estimatedFeeAmount.Value;

        if (_sendingAmount == 0)
          _sendingFee = 0;
      }
      this.CallUIRefresh();
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


    public BsonMapper _bsonMapper;
    public void CreateBsonMapper()
    {
      ICurrencies currencies = AccountStorage.Currencies;

      Console.WriteLine("Creating bson mapper");
      _bsonMapper = new BsonMapper()
                .UseSerializer(new CurrencyToBsonSerializer(currencies))
                .UseSerializer(new BigIntegerToBsonSerializer())
                .UseSerializer(new JObjectToBsonSerializer())
                .UseSerializer(new WalletAddressToBsonSerializer())
                .UseSerializer(new OrderToBsonSerializer())
                .UseSerializer(new BitcoinBasedTransactionToBsonSerializer(currencies))
                .UseSerializer(new BitcoinBasedTxOutputToBsonSerializer())
                .UseSerializer(new EthereumTransactionToBsonSerializer())
                .UseSerializer(new TezosTransactionToBsonSerializer())
                .UseSerializer(new SwapToBsonSerializer(currencies));
    }


    public void ResetSendData()
    {
      this.SendingToAddress = "";
      this._sendingAmount = 0;
      this._sendingFee = 0;
      this._sendingFeePrice = _selectedCurrency.GetDefaultFeePrice();
      this._useDefaultFee = true;
      this.CallUIRefresh();
    }
  }
}