using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Atomex;
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
using Atomex.Blockchain.BitcoinBased;

namespace atomex_frontend.Storages
{

  public class WalletStorage
  {
    public WalletStorage(AccountStorage accountStorage)
    {
      this.accountStorage = accountStorage;
    }

    public event Action RefreshRequested;
    public void CallUIRefresh()
    {
      RefreshRequested?.Invoke();
    }

    private AccountStorage accountStorage;
    public List<Currency> AvailableCurrencies { get => accountStorage.Account.Currencies.ToList(); }
    public Dictionary<Currency, CurrencyData> PortfolioData { get; } = new Dictionary<Currency, CurrencyData>();
    public List<Transaction> Transactions { get; } = new List<Transaction>();
    public List<Transaction> SelectedCurrencyTransactions
    {
      get => this.Transactions.FindAll(tx => tx.Currency == SelectedCurrency);
    }

    public decimal GetTotalDollars
    {
      get => PortfolioData.Values.Sum(x => x.DollarValue);
    }

    public CurrencyData SelectedCurrencyData
    {
      get => PortfolioData.TryGetValue(SelectedCurrency, out CurrencyData data) ? data : new CurrencyData(AccountStorage.Bitcoin, 0.0m, 0.0m, 0.0m);
    }

    private Currency _selectedCurrency = AccountStorage.Bitcoin;
    public Currency SelectedCurrency
    {
      get => this._selectedCurrency;
      set
      {
        this._selectedCurrency = value;
        this.CallUIRefresh();
      }
    }

    private Currency _selectedSecondCurrency = AccountStorage.Litecoin;
    public Currency SelectedSecondCurrency
    {
      get => _selectedSecondCurrency;
      set { _selectedSecondCurrency = value; }
    }

    public void Initialize()
    {
      if (accountStorage.AtomexApp != null)
      {
        if (accountStorage.AtomexApp.HasQuotesProvider)
        {
          accountStorage.AtomexApp.QuotesProvider.QuotesUpdated += async (object sender, EventArgs args) => await UpdatePortfolioAsync();
        }
        accountStorage.AtomexApp.Account.BalanceUpdated += async (object sender, CurrencyEventArgs args) => await UpdatePortfolioAsync();
      }
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
      await new HdWalletScanner(accountStorage.Account)
          .ScanAsync(currency.Name)
          .ConfigureAwait(false);
    }

    public async Task<WalletAddress> GetFreeAddress(Currency currency)
    {
      return await accountStorage.Account.GetFreeExternalAddressAsync(currency.Name);
    }

    public async Task UpdatePortfolioAsync()
    {
      List<Currency> currenciesList = accountStorage.Account.Currencies.ToList();

      foreach (Currency currency in currenciesList)
      {
        decimal availableBalance = (await accountStorage.Account.GetBalanceAsync(currency.Name)).Available;
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
        currencyData.Percent = this.GetTotalDollars != 0 ? currencyData.DollarValue / this.GetTotalDollars * 100.0m : 0;
      }

      this.CallUIRefresh();
    }

    public async Task GetTransactions(Currency currency)
    {
      var transactions = await accountStorage.Account.GetTransactionsAsync(currency.Name);
      foreach (var tx in transactions)
      {
        switch (tx.Currency)
        {
          case BitcoinBasedCurrency _:
            IBitcoinBasedTransaction btcBasedTrans = (IBitcoinBasedTransaction)tx;
            AddTransaction(new Transaction(currency, btcBasedTrans.Id, btcBasedTrans.State, btcBasedTrans.Type, btcBasedTrans.CreationTime, btcBasedTrans.IsConfirmed, btcBasedTrans.Amount), currency);
            break;
          case Ethereum _:
            EthereumTransaction ethTrans = (EthereumTransaction)tx;
            AddTransaction(new Transaction(currency, ethTrans.Id, ethTrans.State, ethTrans.Type, ethTrans.CreationTime, ethTrans.IsConfirmed, 0.01m), currency);
            break;
          case Tezos _:
            TezosTransaction xtzTrans = (TezosTransaction)tx;
            AddTransaction(new Transaction(currency, xtzTrans.Id, xtzTrans.State, xtzTrans.Type, xtzTrans.CreationTime, xtzTrans.IsConfirmed, xtzTrans.Amount), currency);
            break;
        }
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

    private decimal GetDollarValue(Currency currency, decimal amount)
    {
      if (accountStorage.QuotesProvider != null)
      {
        return accountStorage.QuotesProvider.GetQuote(currency.Name, "USD").Bid * amount;
      }
      return 0.0m;
    }

  }
}