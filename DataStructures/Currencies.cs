using Atomex.Core;

namespace atomex_frontend.atomex_data_structures
{
  public class CurrencyData
  {
    public CurrencyData(CurrencyConfig currencyConfig, decimal balance, decimal dollarValue, decimal percent)
    {
      CurrencyConfig = currencyConfig;
      Balance = balance;
      DollarValue = dollarValue;
      Percent = percent;
    }

    public CurrencyConfig CurrencyConfig;

    public decimal Balance;

    public decimal DollarValue;

    public decimal Percent;
  }
}