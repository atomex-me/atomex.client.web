using System;
using Atomex;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;

namespace atomex_frontend.atomex_data_structures
{
    public class TezosTokenTransferViewModel : Transaction
    {
        public const int MaxAmountDecimals = 9;
        private readonly TezosConfig _tezosConfig;
        public IBlockchainTransaction Transaction { get; }
        public string AmountFormat { get; set; }
        public string CurrencyCode { get; set; }
        public DateTime Time { get; set; }
        public DateTime LocalTime => Time.ToLocalTime();
        public string TxExplorerUri => $"{_tezosConfig.TxExplorerUri}{Id}";
        public string FromExplorerUri => $"{_tezosConfig.AddressExplorerUri}{From}";
        public string ToExplorerUri => $"{_tezosConfig.AddressExplorerUri}{To}";
        
        public TezosTokenTransferViewModel(TokenTransfer tx, TezosConfig tezosConfig) :
            base(tezosConfig, tx.Id, tx.State, tx.Type, tx.CreationTime, tx.IsConfirmed, 0, alias: tx.GetAlias())
        {
            _tezosConfig = tezosConfig;
            Transaction  = tx ?? throw new ArgumentNullException(nameof(tx));
            //Id           = tx.Hash;
            State        = Transaction.State;
            Type         = Transaction.Type;
            From         = tx.From;
            To           = tx.To;
            Amount       = GetAmount(tx);
            AmountFormat = $"F{Math.Min(tx.Token.Decimals, MaxAmountDecimals)}";
            CurrencyCode = tx.Token.Symbol;
            Time         = tx.CreationTime ?? DateTime.UtcNow;

            Description = GetDescription(
                type: tx.Type,
                amount: Amount,
                netAmount: Amount,
                amountDigits: tx.Token.Decimals,
                currencyCode: tx.Token.Symbol);
        }
        
        private static decimal GetAmount(TokenTransfer tx)
        {
            if (tx.Amount.TryParseWithRound(tx.Token.Decimals, out var amount))
            {
                var sign = tx.Type.HasFlag(BlockchainTransactionType.Input)
                    ? 1
                    : -1;

                return sign * amount;
            }

            return 0;
        }
    }
}