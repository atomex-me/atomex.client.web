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

namespace atomex_frontend.Storages
{

  public class WalletStorage
  {
    public WalletStorage(
      AccountStorage accountStorage,
      BakerStorage bakerStorage,
      IJSRuntime JSRuntime,
      NavigationManager uriHelper)
    {
      this.accountStorage = accountStorage;
      this.bakerStorage = bakerStorage;
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
    public List<Transaction> Transactions { get; set; } = new List<Transaction>();
    public List<Transaction> SelectedCurrencyTransactions
    {
      get => this.Transactions
        .FindAll(tx => tx.Currency.Name == SelectedCurrency.Name)
        .OrderByDescending(a => a.CreationTime)
        .ToList();
    }

    public decimal GetTotalDollars
    {
      get => Helper.SetPrecision(PortfolioData.Values.Sum(x => x.DollarValue), 1);
    }

    public CurrencyData SelectedCurrencyData
    {
      get => PortfolioData.TryGetValue(SelectedCurrency.Name, out CurrencyData data) ? data : new CurrencyData(AccountStorage.Bitcoin, 0.0m, 0.0m, 0.0m);
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


    public async void Initialize(bool IsRestarting)
    {
      if (accountStorage.AtomexApp != null)
      {
        SelectedCurrency = AccountStorage.Bitcoin;
        if (accountStorage.AtomexApp.HasQuotesProvider && !IsRestarting)
        {
          accountStorage.AtomexApp.QuotesProvider.QuotesUpdated += async (object sender, EventArgs args) => await UpdatePortfolioAsync();
        }
        accountStorage.AtomexApp.Account.BalanceUpdated += async (object sender, CurrencyEventArgs args) =>
        {
          if (args.Currency == "XTZ")
          {
            bakerStorage.LoadDelegationInfoAsync().FireAndForget();
          }
          await UpdatePortfolioAsync();
        };
        accountStorage.AtomexApp.Account.UnconfirmedTransactionAdded += OnUnconfirmedTransactionAddedEventHandler;

        List<Currency> currenciesList = accountStorage.Account.Currencies.ToList();
        Transactions = new List<Transaction>();
        PortfolioData = new Dictionary<string, CurrencyData>();


        foreach (Currency currency in currenciesList)
        {
          CurrencyData initialCurrencyData = new CurrencyData(currency, 0, 0, 0.0m);
          PortfolioData.Add(currency.Name, initialCurrencyData);
          Console.WriteLine("Getting free address on initialize");
          initialCurrencyData.FreeExternalAddress = (await this.accountStorage.Account.GetFreeExternalAddressAsync(initialCurrencyData.Currency.Name)).Address;
          GetFreeAddresses(initialCurrencyData.Currency);
        }

        await UpdatePortfolioAsync();
        URIHelper.NavigateTo("/wallet");
        accountStorage.WalletLoading = false;
        CurrentWalletSection = WalletSection.Portfolio;
        SelectedCurrency = AccountStorage.Bitcoin;
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

    public async Task UpdatePortfolioAsync()
    {
      List<Currency> currenciesList = accountStorage.Account.Currencies.ToList();

      foreach (Currency currency in currenciesList)
      {
        Balance balance = (await accountStorage.Account.GetBalanceAsync(currency.Name));
        var availableBalance = balance.Available;
        if (!PortfolioData.TryGetValue(currency.Name, out CurrencyData currencyData))
        {
          PortfolioData.Add(currency.Name, new CurrencyData(currency, availableBalance, this.GetDollarValue(currency, availableBalance), 0.0m));
        }
        else
        {
          currencyData.Balance = availableBalance;
          currencyData.DollarValue = this.GetDollarValue(currency, availableBalance);
        }

        await GetTransactions(currency);
      }

      FromAddressList = new List<WalletAddressView>();
      foreach (CurrencyData currencyData in PortfolioData.Values)
      {
        currencyData.Percent = this.GetTotalDollars != 0 ? Helper.SetPrecision(currencyData.DollarValue / this.GetTotalDollars * 100.0m, 2) : 0;
        currencyData.FreeExternalAddress = (await this.accountStorage.Account.GetFreeExternalAddressAsync(currencyData.Currency.Name)).Address;

        GetFreeAddresses(currencyData.Currency);
      }

      this.CallUIRefresh();

      if (CurrentWalletSection == WalletSection.Portfolio)
      {
        await DrawDonutChart(updateData: true);
      }
    }

    private void GetFreeAddresses(Currency Currency)
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
              isInternal: IsInternalFa12
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
              isInternal: IsInternalXtz
            ), tx.Currency);
          break;
      }
    }

    public decimal GetDollarValue(Currency currency, decimal amount)
    {
      if (accountStorage.QuotesProvider != null)
      {
        return Helper.SetPrecision(accountStorage.QuotesProvider.GetQuote(currency.Name, "USD").Bid * amount, 4);
      }
      return 0;
    }

    protected async void UpdateSendingAmount(decimal amount)
    {
      bool TetherOrFa12 = SelectedCurrency is FA12 || SelectedCurrency is Tether;

      if ((SelectedCurrency is FA12 || SelectedCurrency is Tezos) && !SelectedCurrency.IsValidAddress(SendingToAddress))
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

        if ((feeAmount > estimatedFeeAmount.Value) && !(SelectedCurrency is Tether) && !(SelectedCurrency is FA12))
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