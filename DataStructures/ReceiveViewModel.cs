using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atomex;
using atomex_frontend.Components;

// using QRCoder;
using Serilog;
using Atomex.Core;
using Atomex.Common;
using Atomex.Wallet.Abstract;

namespace atomex_frontend.atomex_data_structures
{
    public class ReceiveViewModel
    {
        private readonly IAtomexApp _app;

        protected CurrencyConfig _currency;

        public virtual CurrencyConfig Currency
        {
            get => _currency;
            set
            {
                _currency = value;

                    // get all addresses with tokens (if exists)
                    var tokenAddresses = Currencies.HasTokens(_currency.Name)
                        ? _app.Account
                            .GetCurrencyAccount<ILegacyCurrencyAccount>(_currency.Name)
                            .GetUnspentTokenAddressesAsync()
                            .WaitForResult()
                        : new List<WalletAddress>();

                    // get all active addresses
                    var activeAddresses = _app.Account
                        .GetUnspentAddressesAsync(_currency.Name)
                        .WaitForResult()
                        .ToList();

                    // get free external address
                    var freeAddress = _app.Account
                        .GetFreeExternalAddressAsync(_currency.Name)
                        .WaitForResult();

                    FromAddressList = activeAddresses
                        .Concat(tokenAddresses)
                        .Concat(new WalletAddress[] {freeAddress})
                        .GroupBy(w => w.Address)
                        .Select(g =>
                        {
                            // main address
                            var address = g.FirstOrDefault(w => w.Currency == _currency.Name);

                            var isFreeAddress = address.Address == freeAddress.Address;

                            var hasTokens = g.Any(w => w.Currency != _currency.Name);

                            var tokenAddresses = TokenContract != null
                                ? g.Where(w => w.TokenBalance?.Contract == TokenContract)
                                : Enumerable.Empty<WalletAddress>();

                            var hasSeveralTokens = tokenAddresses.Count() > 1;

                            var tokenAddress = tokenAddresses.FirstOrDefault();

                            var tokenBalance = hasSeveralTokens
                                ? tokenAddresses.Count()
                                : tokenAddress?.Balance ?? 0m;

                            var showTokenBalance = hasSeveralTokens
                                ? tokenBalance != 0
                                : TokenContract != null && tokenAddress?.TokenBalance?.Symbol != null;

                            var tokenCode = hasSeveralTokens
                                ? "TOKENS"
                                : tokenAddress?.TokenBalance?.Symbol ?? "";

                            return new WalletAddressViewModel
                            {
                                Address = g.Key,
                                HasActivity = address?.HasActivity ?? hasTokens,
                                AvailableBalance = address?.AvailableBalance() ?? 0m,
                                CurrencyFormat = _currency.Format,
                                CurrencyCode = _currency.Name,
                                IsFreeAddress = isFreeAddress,
                                ShowTokenBalance = showTokenBalance,
                                TokenBalance = tokenBalance,
                                TokenFormat = "F8",
                                TokenCode = tokenCode
                            };
                        })
                        .ToList();
            }
        }

        private List<WalletAddressViewModel> _fromAddressList;

        public List<WalletAddressViewModel> FromAddressList
        {
            get => _fromAddressList;
            protected set
            {
                _fromAddressList = value;

                SelectedAddress = GetDefaultAddress();
            }
        }

        private string _selectedAddress;

        public string SelectedAddress
        {
            get => _selectedAddress;
            set
            {
                _selectedAddress = value;

                // if (_selectedAddress != null)
                //     _ = CreateQrCodeAsync();

                Warning = string.Empty;
            }
        }

        private string _warning;

        public string Warning
        {
            get => _warning;
            set
            {
                _warning = value;
            }
        }

        public string TokenContract { get; private set; }

        public ReceiveViewModel(IAtomexApp app, CurrencyConfig currency, string tokenContract = null)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            
            TokenContract = tokenContract;
            Currency = currency;
            
            Console.WriteLine($"Created ReceiveViewModel with curr {Currency.Name} and TokenContr {TokenContract}");
        }

        protected virtual string GetDefaultAddress()
        {
            if (Currency is TezosConfig || Currency is EthereumConfig)
            {
                var activeAddressViewModel = FromAddressList
                    .Where(vm => vm.HasActivity && vm.AvailableBalance > 0)
                    .MaxByOrDefault(vm => vm.AvailableBalance);

                if (activeAddressViewModel != null)
                    return activeAddressViewModel.Address;
            }

            return FromAddressList.First(vm => vm.IsFreeAddress).Address;
        }
    }
}