using System;
using System.IO;
using System.Globalization;
using Atomex;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.BitcoinBased;
using Atomex.EthereumTokens;
using Atomex.TezosTokens;
using atomex_frontend.Storages;

namespace atomex_frontend.Common
{
  public static class Helper
  {
    public static Stream GenerateStreamFromString(string s)
    {
      var stream = new MemoryStream();
      var writer = new StreamWriter(stream);
      writer.Write(s);
      writer.Flush();
      stream.Position = 0;
      return stream;
    }

    public static decimal SetPrecision(decimal value, int precision)
    {
      return decimal.Round(value, precision, MidpointRounding.AwayFromZero);
    }

    public static decimal StrToDecimal(string str)
    {
      string res = str.Replace(".", ",");
      decimal number;
      return decimal.TryParse(res, out number) ? number : 0;
    }

    public static string DecimalToStr(decimal value)
    {
      return value.ToString().Replace(",", ".");
    }

    public static string DecimalToStr(decimal value, string format)
    {
      return value.ToString(format, CultureInfo.InvariantCulture).Replace(",", ".");
    }
  }

  public static class CurrHelper
  {
    public static decimal GetTransAmount(EthereumTransaction tx)
    {
      if (tx.Currency.Name == AccountStorage.Tether.Name)
      {
        var Erc20 = tx.Currency as ERC20;
        var usdtResult = 0m;

        if (tx.Type.HasFlag(BlockchainTransactionType.Input))
          usdtResult += Erc20.TokenDigitsToTokens(tx.Amount);

        if (tx.Type.HasFlag(BlockchainTransactionType.Output))
          usdtResult += -Erc20.TokenDigitsToTokens(tx.Amount);

        tx.InternalTxs?.ForEach(t => usdtResult += GetTransAmount(t));
        return usdtResult;
      }

      var result = 0m;

      if (tx.Type.HasFlag(BlockchainTransactionType.Input))
        result += Ethereum.WeiToEth(tx.Amount);

      if (tx.Type.HasFlag(BlockchainTransactionType.Output))
        result += -Ethereum.WeiToEth(tx.Amount + tx.GasUsed * tx.GasPrice);

      return result;
    }

    public static decimal GetTransAmount(TezosTransaction tx)
    {
      var result = 0m;

      if (tx.Type.HasFlag(BlockchainTransactionType.Input))
        result += Tezos.MtzToTz(tx.Amount);

      if (tx.Type.HasFlag(BlockchainTransactionType.Output))
        result += -Tezos.MtzToTz(tx.Amount + tx.Fee);

      return result;
    }

    public static decimal GetTransAmount(IBitcoinBasedTransaction tx)
    {
      return tx.Amount / (decimal)tx.Currency.DigitsMultiplier;
    }

    public static string GetTransDescription(IBlockchainTransaction tx, decimal Amount)
    {
      string Description = "Unknown transaction";
      if (tx.Type.HasFlag(BlockchainTransactionType.SwapPayment))
      {
        Description = $"Swap payment {Math.Abs(Amount).ToString(CultureInfo.InvariantCulture)} {tx.Currency.Name}";
      }
      else if (tx.Type.HasFlag(BlockchainTransactionType.SwapRefund))
      {
        Description = $"Swap refund {Math.Abs(Amount).ToString(CultureInfo.InvariantCulture)} {tx.Currency.Name}";
      }
      else if (tx.Type.HasFlag(BlockchainTransactionType.SwapRedeem))
      {
        Description = $"Swap redeem {Math.Abs(Amount).ToString(CultureInfo.InvariantCulture)} {tx.Currency.Name}";
      }
      else if (tx.Type.HasFlag(BlockchainTransactionType.TokenApprove))
      {
        Description = $"Token approve";
      }
      else if (Amount < 0) //tx.Type.HasFlag(BlockchainTransactionType.Output))
      {
        Description = $"Sent {Math.Abs(Amount).ToString(CultureInfo.InvariantCulture)} {tx.Currency.Name}";
      }
      else if (Amount >= 0) //tx.Type.HasFlag(BlockchainTransactionType.Input)) // has outputs
      {
        Description = $"Received {Math.Abs(Amount).ToString(CultureInfo.InvariantCulture)} {tx.Currency.Name}";
      }

      return Description;
    }
  }
}