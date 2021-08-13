using System;
using System.Windows.Input;

using Serilog;

using Atomex;
using Atomex.Blockchain.Tezos;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Tezos;
using System.Linq;
using System.Threading.Tasks;
using Atomex.TezosTokens;


namespace atomex_frontend.atomex_data_structures
{
    public class SendConfirmationViewModel
    {
        public IAtomexApp App;
        public CurrencyConfig Currency { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string CurrencyFormat { get; set; }
        public string BaseCurrencyFormat { get; set; }
        public decimal Amount { get; set; }
        public decimal Fee { get; set; }
        public decimal FeePrice { get; set; }
        public decimal FeeAmount => Currency.GetFeeAmount(Fee, FeePrice);
        public bool UseDeafultFee { get; set; }
        public decimal AmountInBase { get; set; }
        public decimal FeeInBase { get; set; }
        public string CurrencyCode { get; set; }
        public string BaseCurrencyCode { get; set; }
        public string FeeCurrencyCode { get; set; }
        public string FeeCurrencyFormat { get; set; }
        public string TokenContract { get; set; }
        public decimal TokenId { get; set; }

        public Error Error { get; set; }

        public async Task Send()
        {
            try
            {
                if (From != null && TokenContract != null) // tezos token sending
                {
                    var tokenAddress = await TezosTokensSendViewModel.GetTokenAddressAsync(
                        App.Account,
                        From,
                        TokenContract,
                        TokenId);

                    if (tokenAddress.Currency == "FA12")
                    {
                        var currencyName = App.Account.Currencies
                            .FirstOrDefault(c => c is Fa12Config fa12 && fa12.TokenContractAddress == TokenContract)
                            ?.Name ?? "FA12";

                        var tokenAccount = App.Account
                            .GetTezosTokenAccount<Fa12Account>(currencyName, TokenContract, TokenId);

                        Error = await tokenAccount
                            .SendAsync(new WalletAddress[] { tokenAddress }, To, Amount, Fee, FeePrice, UseDeafultFee);
                    }
                    else
                    {
                        var tokenAccount = App.Account
                            .GetTezosTokenAccount<Fa2Account>("FA2", TokenContract, TokenId);

                        var decimals = tokenAddress.TokenBalance.Decimals;
                        var amount = Amount * (decimal)Math.Pow(10, decimals);
                        var fee = (int)Fee.ToMicroTez();

                        Error = await tokenAccount.SendAsync(
                            from: From,
                            to: To,
                            amount: amount,
                            tokenContract: TokenContract,
                            tokenId: (int)TokenId,
                            fee: fee,
                            useDefaultFee: UseDeafultFee);
                    }
                }
                else
                {
                    var account = App.Account
                        .GetCurrencyAccount<ILegacyCurrencyAccount>(Currency.Name);

                    Error = await account
                        .SendAsync(To, Amount, Fee, FeePrice, UseDeafultFee);
                }
            }
            catch (Exception e)
            {
                Error = new Error(Errors.RequestError, "An error has occurred while sending transaction.");
                Log.Error(e, "Transaction send error.");
            }
        }
        
    }
}