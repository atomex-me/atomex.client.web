using Atomex.Core;
using System;
using System.Threading.Tasks;
using Atomex.Abstract;
using Atomex.Common;
using Atomex.Common.Configuration;
using Atomex.Subsystems.Abstract;
using Microsoft.Extensions.Configuration;

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