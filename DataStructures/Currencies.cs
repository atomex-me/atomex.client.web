namespace atomex_frontend.atomex_data_structures
{
  public enum AvailableCurrencies
  {
    Bitcoin,
    Ethereum,
    Litecoin,
    Tezos
  }

  public class CurrencyData
  {
    public CurrencyData(AvailableCurrencies currencyName, double balance, double dollarValue, double percent)
    {
      CurrencyName = currencyName;
      Balance = balance;
      DollarValue = dollarValue;
      Percent = percent;
    }

    public AvailableCurrencies CurrencyName;
    public double Balance;

    public double DollarValue;

    public double Percent;
  }

}