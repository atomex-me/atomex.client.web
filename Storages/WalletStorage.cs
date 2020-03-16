using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Core;
using Atomex.Wallet;
using Atomex.Abstract;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using atomex_frontend.atomex_data_structures;
using Atomex.MarketData.Abstract;

namespace atomex_frontend.Storages
{

  public class WalletStorage
  {
    public WalletStorage(AccountStorage accountStorage)
    {
      this.accountStorage = accountStorage;
    }

    private AccountStorage accountStorage;
    public List<Currency> AvailableCurrencies { get => accountStorage.Account.Currencies.ToList(); }
    public Dictionary<AvailableCurrencies, CurrencyData> PortfolioData { get; } = new Dictionary<AvailableCurrencies, CurrencyData>();

    private CurrencyData BtcPortfolio { get; set; }
    private CurrencyData EthPortfolio { get; set; }
    private CurrencyData LtcPortfolio { get; set; }
    private CurrencyData XtzPortfolio { get; set; }




    public async Task ScanCurrencyAsync(Currency currency)
    {
      Console.WriteLine($"Scanning {currency.Name}");
      await new HdWalletScanner(accountStorage.Account)
          .ScanAsync(currency.Name)
          .ConfigureAwait(false);
    }

    public async Task<WalletAddress> GetFreeAddress(string currency)
    {
      WalletAddress wa = await accountStorage.Account.GetFreeExternalAddressAsync(currency);
      Console.WriteLine(wa.ToString());
      return wa;
    }

    public async void UpdatePortfolioAsync()
    {
      List<Currency> currenciesList = accountStorage.Account.Currencies.ToList();

      Dictionary<Currency, decimal> balances = new Dictionary<Currency, decimal>();



      foreach (Currency currency in currenciesList)
      {
        decimal availableBalance = (await accountStorage.Account.GetBalanceAsync(currency.Name)).Available;
        balances.Add(currency, availableBalance);
      }
    }

    public async Task<List<IBlockchainTransaction>> GetTezosBalance()
    {
      // var balance = await accountStorage.Account.GetBalanceAsync("XTZ");

      var transactions = await accountStorage.Account.GetTransactionsAsync("LTC");
      return transactions.ToList();

    }

  }
}