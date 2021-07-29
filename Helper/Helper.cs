using System;
using System.IO;
using System.Globalization;
using System.Threading.Tasks;
using Atomex;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.BitcoinBased;
using Atomex.EthereumTokens;
using atomex_frontend.atomex_data_structures;
using Atomex.Core;
using Atomex.TezosTokens;
using Atomex.Wallet.Abstract;

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
            decimal number;
            return decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out number) ? number : 0;
        }

        public static string DecimalToStr(decimal value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string IntToStr(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string DecimalToStr(decimal value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        public static void RunAsync(Task task)
        {
            task.ContinueWith(t => { Console.WriteLine("Unexpected Error", t.Exception); },
                TaskContinuationOptions.OnlyOnFaulted);
        }

        public static decimal TruncateByFormat(decimal d, string format)
        {
            var s = d.ToString(format, CultureInfo.InvariantCulture);

            return decimal.Parse(s, CultureInfo.InvariantCulture);
        }
    }

    public static class CurrHelper
    {
        public static decimal GetTransAmount(EthereumTransaction tx, CurrencyConfig currencyConfig)
        {
            if (tx.Currency == "USDT" || tx.Currency == "TBTC" || tx.Currency == "WBTC")
            {
                var Erc20 = currencyConfig as Erc20Config;
                var usdtResult = 0m;

                if (tx.Type.HasFlag(BlockchainTransactionType.SwapRedeem) ||
                    tx.Type.HasFlag(BlockchainTransactionType.SwapRefund))
                    usdtResult += Erc20.TokenDigitsToTokens(tx.Amount);
                else
                {
                    if (tx.Type.HasFlag(BlockchainTransactionType.Input))
                        usdtResult += Erc20.TokenDigitsToTokens(tx.Amount);
                    if (tx.Type.HasFlag(BlockchainTransactionType.Output))
                        usdtResult += -Erc20.TokenDigitsToTokens(tx.Amount);
                }

                tx.InternalTxs?.ForEach(t => usdtResult += GetTransAmount(t, currencyConfig));
                return usdtResult;
            }

            var result = 0m;

            if (tx.Type.HasFlag(BlockchainTransactionType.Input))
                result += EthereumConfig.WeiToEth(tx.Amount);

            if (tx.Type.HasFlag(BlockchainTransactionType.Output))
                result += -EthereumConfig.WeiToEth(tx.Amount + tx.GasUsed * tx.GasPrice);

            tx.InternalTxs?.ForEach(t => result += GetTransAmount(t, currencyConfig));

            return result;
        }

        public static decimal GetFee(EthereumTransaction tx)
        {
            var result = 0m;

            if (tx.Type.HasFlag(BlockchainTransactionType.Output))
                result += EthereumConfig.WeiToEth(tx.GasUsed * tx.GasPrice);

            tx.InternalTxs?.ForEach(t => result += GetFee(t));

            return result;
        }

        public static decimal GetTransAmount(TezosTransaction tx, CurrencyConfig currencyConfig)
        {
            if (currencyConfig is Fa12Config)
            {
                var fa12Result = 0m;

                if (tx.Type.HasFlag(BlockchainTransactionType.SwapRedeem) ||
                    tx.Type.HasFlag(BlockchainTransactionType.SwapRefund))
                    fa12Result += tx.Amount.FromTokenDigits(currencyConfig.DigitsMultiplier);
                else
                {
                    if (tx.Type.HasFlag(BlockchainTransactionType.Input))
                        fa12Result += tx.Amount.FromTokenDigits(currencyConfig.DigitsMultiplier);
                    if (tx.Type.HasFlag(BlockchainTransactionType.Output))
                        fa12Result += -tx.Amount.FromTokenDigits(currencyConfig.DigitsMultiplier);
                }

                tx.InternalTxs?.ForEach(t => fa12Result += GetTransAmount(t, currencyConfig));

                return fa12Result;
            }

            var result = 0m;

            if (tx.Type.HasFlag(BlockchainTransactionType.Input))
                result += tx.Amount / currencyConfig.DigitsMultiplier;

            var includeFee = tx.Currency == currencyConfig.FeeCurrencyName;
            var fee = includeFee ? tx.Fee : 0;

            if (tx.Type.HasFlag(BlockchainTransactionType.Output))
                result += -(tx.Amount + fee) / currencyConfig.DigitsMultiplier;

            tx.InternalTxs?.ForEach(t => result += GetTransAmount(t, currencyConfig));

            return result;
        }

        public static decimal GetFee(TezosTransaction tx)
        {
            var result = 0m;

            if (tx.Type.HasFlag(BlockchainTransactionType.Output))
                result += TezosConfig.MtzToTz(tx.Fee);

            tx.InternalTxs?.ForEach(t => result += GetFee(t));

            return result;
        }

        public static decimal GetTransAmount(IBitcoinBasedTransaction tx, CurrencyConfig currencyConfig)
        {
            return tx.Amount / currencyConfig.DigitsMultiplier;
        }

        public static decimal GetFee(IBitcoinBasedTransaction tx, CurrencyConfig currencyConfig)
        {
            return tx.Fees != null
                ? tx.Type.HasFlag(BlockchainTransactionType.Output)
                    ? tx.Fees.Value / currencyConfig.DigitsMultiplier
                    : 0
                : 0;
        }
        
        public static string GetTxDirection(Transaction tx)
        {
            if (tx.Amount <= 0)
            {
                return "To";
            }

            if (tx.Amount > 0)
            {
                return "From";
            }

            return String.Empty;
        }

        public static string GetTxAlias(Transaction tx)
        {
            if (!String.IsNullOrEmpty(tx.Alias))
            {
                return tx.Alias;
            }
            else
            {
                if (tx.Amount <= 0)
                {
                    return tx.To;
                }

                if (tx.Amount > 0)
                {
                    return tx.From;
                }
            }

            return String.Empty;
        }
    }
}