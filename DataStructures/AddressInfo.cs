using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Atomex.Core;
using Atomex.Wallet;
using Atomex.Wallet.Tezos;
using Atomex;
using Atomex.Blockchain.Tezos.Internal;
using Atomex.Common;
using Atomex.Cryptography;

namespace atomex_frontend.atomex_data_structures
{
    public class AddressInfo
    {
        public string Address { get; set; }
        public string Type { get; set; }
        public string Path { get; set; }
        public string Balance { get; set; }
        public string TokenBalance { get; set; }
    }

    public class AddressesViewModel
    {
        private readonly IAtomexApp _app;

        public CurrencyConfig Currency;
        private bool _isBalanceUpdating;
        private CancellationTokenSource _cancellation;
        private readonly string _tokenContract;

        public IList<AddressInfo> Addresses { get; set; }
        public bool HasTokens { get; set; }

        private string _warning;

        public string Warning
        {
            get => _warning;
            set { _warning = value; }
        }

        public bool HasWarning => !string.IsNullOrEmpty(Warning);

        public event Action UIRefresh;

        private void CallUIRefresh()
        {
            UIRefresh?.Invoke();
        }

        public AddressesViewModel(
            IAtomexApp app,
            CurrencyConfig currency,
            string tokenContract = null)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            _tokenContract = tokenContract;

            RealodAddresses();
        }

        public async void RealodAddresses()
        {
            try
            {
                var account = _app.Account
                    .GetCurrencyAccount(Currency.Name);

                var addresses = (await account
                        .GetAddressesAsync())
                    .ToList();

                addresses.Sort((a1, a2) =>
                {
                    var typeResult = a1.KeyType.CompareTo(a2.KeyType);

                    if (typeResult != 0)
                        return typeResult;

                    var accountResult = a1.KeyIndex.Account.CompareTo(a2.KeyIndex.Account);

                    if (accountResult != 0)
                        return accountResult;

                    var chainResult = a1.KeyIndex.Chain.CompareTo(a2.KeyIndex.Chain);

                    return chainResult != 0
                        ? chainResult
                        : a1.KeyIndex.Index.CompareTo(a2.KeyIndex.Index);
                });

                Addresses = new List<AddressInfo>(
                    addresses.Select(a =>
                    {
                        var path = a.KeyType == CurrencyConfig.StandardKey && Currencies.IsTezosBased(Currency.Name)
                            ? $"m/44'/{Currency.Bip44Code}'/{a.KeyIndex.Account}'/{a.KeyIndex.Chain}'"
                            : $"m/44'/{Currency.Bip44Code}'/{a.KeyIndex.Account}'/{a.KeyIndex.Chain}/{a.KeyIndex.Index}";

                        return new AddressInfo
                        {
                            Address = a.Address,
                            Type = KeyTypeToString(a.KeyType),
                            Path = path,
                            Balance = $"{a.Balance.ToString(CultureInfo.InvariantCulture)} {Currency.Name}",
                        };
                    }));

                // token balances
                if (Currency.Name == TezosConfig.Xtz && _tokenContract != null)
                {
                    HasTokens = true;

                    var tezosAccount = account as TezosAccount;

                    var addressesWithTokens = (await tezosAccount
                            .DataRepository
                            .GetTezosTokenAddressesByContractAsync(_tokenContract))
                        .Where(w => w.Balance != 0)
                        .GroupBy(w => w.Address);

                    foreach (var addressWithTokens in addressesWithTokens)
                    {
                        var addressInfo = Addresses.FirstOrDefault(a => a.Address == addressWithTokens.Key);

                        if (addressInfo == null)
                            continue;

                        if (addressWithTokens.Count() == 1)
                        {
                            var tokenAddress = addressWithTokens.First();

                            addressInfo.TokenBalance =
                                tokenAddress.Balance.ToString("F8", CultureInfo.InvariantCulture);

                            var tokenCode = tokenAddress?.TokenBalance?.Symbol;

                            if (tokenCode != null)
                                addressInfo.TokenBalance += $" {tokenCode}";
                        }
                        else
                        {
                            addressInfo.TokenBalance = $"{addressWithTokens.Count()} TOKENS";
                        }
                    }
                }

                CallUIRefresh();
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while reload addresses list.");
            }
        }

        private string KeyTypeToString(int keyType) =>
            keyType switch
            {
                CurrencyConfig.StandardKey  => "Standard",
                TezosConfig.Bip32Ed25519Key => "Atomex",
                _ => throw new NotSupportedException($"Key type {keyType} not supported.")
            };
        
        public string GetExplorerUrl(string address)
        {
            try
            {
                if (Uri.TryCreate($"{Currency.AddressExplorerUri}{address}", UriKind.Absolute, out var uri))
                    return uri.ToString();

                Log.Error("Invalid uri for address explorer");
            }
            catch (Exception e)
            {
                Log.Error(e, "Open in explorer error");
            }

            return string.Empty;
        }

        public async Task Update(string address)
        {
            if (_isBalanceUpdating)
                return;

            _isBalanceUpdating = true;

            _cancellation = new CancellationTokenSource();

            try
            {
                await new HdWalletScanner(_app.Account)
                    .ScanAddressAsync(Currency.Name, address, _cancellation.Token);

                if (Currency.Name == TezosConfig.Xtz && _tokenContract != null)
                {
                    // update tezos token balance
                    var tezosAccount = _app.Account
                        .GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz);

                    await new TezosTokensScanner(tezosAccount)
                        .ScanContractAsync(address, _tokenContract);

                    // reload balances for all tezos tokens account
                    foreach (var currency in _app.Account.Currencies)
                        if (Currencies.IsTezosToken(currency.Name))
                            _app.Account
                                .GetCurrencyAccount<TezosTokenAccount>(currency.Name)
                                .ReloadBalances();
                }

                RealodAddresses();
            }
            catch (OperationCanceledException)
            {
                Log.Debug("Address balance update operation canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "AddressesViewModel.OnUpdateClick");
                // todo: message to user!?
            }
            
            _isBalanceUpdating = false;
        }

        public async Task<string> ExportKey(string address)
        {
            try
            {
                var walletAddress = await _app.Account
                    .GetAddressAsync(Currency.Name, address);

                var hdWallet = _app.Account.Wallet as HdWallet;

                using var privateKey = hdWallet.KeyStorage.GetPrivateKey(
                    currency: Currency,
                    keyIndex: walletAddress.KeyIndex,
                    keyType: walletAddress.KeyType);

                using var unsecuredPrivateKey = privateKey.ToUnsecuredBytes();
                
                if (Currencies.IsBitcoinBased(Currency.Name))
                {
                    var btcBasedConfig = Currency as BitcoinBasedConfig;

                    return new NBitcoin.Key(unsecuredPrivateKey)
                        .GetWif(btcBasedConfig.Network)
                        .ToWif();
                }
                else if (Currencies.IsTezosBased(Currency.Name))
                {
                    return unsecuredPrivateKey.Length == 32
                        ? Base58Check.Encode(unsecuredPrivateKey, Prefix.Edsk)
                        : Base58Check.Encode(unsecuredPrivateKey, Prefix.EdskSecretKey);
                    
                }
                else
                {
                    return Hex.ToHexString(unsecuredPrivateKey.Data);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Private key export error");
            }

            return string.Empty;
        }
    }
}