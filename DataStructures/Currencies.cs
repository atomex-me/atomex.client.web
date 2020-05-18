using Atomex.Core;

namespace atomex_frontend.atomex_data_structures
{
  public class CurrencyData
  {
    public CurrencyData(Currency currency, decimal balance, decimal dollarValue, decimal percent)
    {
      Currency = currency;
      Balance = balance;
      DollarValue = dollarValue;
      Percent = percent;
    }

    public Currency Currency;

    public decimal Balance;

    public decimal DollarValue;

    public decimal Percent;

    public string FreeExternalAddress = "";
  }
}