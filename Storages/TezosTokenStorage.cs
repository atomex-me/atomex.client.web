using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using System.Threading;
using System.Windows.Input;
using Atomex;
using atomex_frontend.atomex_data_structures;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Services;
using Atomex.Wallet;;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Tezos;
using Serilog;

namespace atomex_frontend.Storages
{
    public class TezosTokenViewModel
    {
        private bool _isPreviewDownloading = false;

        public TokenBalance TokenBalance { get; set; }

        private string _tokenPreview;

        public string TokenPreview
        {
            get
            {
                if (_isPreviewDownloading)
                    return null;

                if (_tokenPreview != null)
                    return _tokenPreview;

                foreach (var url in GetTokenPreviewUrls())
                {
                    _ = Task.Run(async () =>
                    {
                        await FromUrlAsync(url)
                            .ConfigureAwait(false);
                    });
                }

                return null;
            }
        }

        public string Balance => TokenBalance.Balance != "1"
            ? $"{TokenBalance.Balance}  {TokenBalance.Symbol}"
            : "";

        public bool IsIpfsAsset => TokenBalance.ArtifactUri != null && HasIpfsPrefix(TokenBalance.ArtifactUri);

        public string AssetUrl => IsIpfsAsset
            ? $"http://ipfs.io/ipfs/{RemoveIpfsPrefix(TokenBalance.ArtifactUri)}"
            : null;

        private async Task FromUrlAsync(string url)
        {
            _isPreviewDownloading = true;

            try
            {
                var response = await HttpHelper.HttpClient
                    .GetAsync(url)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var previewBytes = await response.Content
                        .ReadAsByteArrayAsync()
                        .ConfigureAwait(false);

                    _tokenPreview = Convert.ToBase64String(previewBytes);

                    // todo: Refresh UI
                }
            }
            catch
            {
                // ignored
            }

            _isPreviewDownloading = false;
        }

        public IEnumerable<string> GetTokenPreviewUrls()
        {
            yield return $"https://d38roug276qjor.cloudfront.net/{TokenBalance.Contract}/{TokenBalance.TokenId}.png";

            if (TokenBalance.ArtifactUri != null && HasIpfsPrefix(TokenBalance.ArtifactUri))
                yield return $"https://api.dipdup.net/thumbnail/{RemoveIpfsPrefix(TokenBalance.ArtifactUri)}";

            yield return $"https://services.tzkt.io/v1/avatars/{TokenBalance.Contract}";
        }

        public static string RemovePrefix(string s, string prefix) =>
            s.StartsWith(prefix) ? s.Substring(prefix.Length) : s;

        public static string RemoveIpfsPrefix(string url) =>
            RemovePrefix(url, "ipfs://");

        public static bool HasIpfsPrefix(string url) =>
            url?.StartsWith("ipfs://") ?? false;
    }

    public class TezosTokenContractViewModel
    {
        public TokenContract Contract { get; set; }
        public string IconUrl => $"https://services.tzkt.io/v1/avatars/{Contract.Address}";
        public bool IsFa12 => Contract.GetContractType() == "FA12";
        public bool IsFa2 => Contract.GetContractType() == "FA2";
    }

    public class TezosTokenStorage
    {
        private const int MaxAmountDecimals = 9;
        private const string Fa12 = "FA12";

        public ObservableCollection<TezosTokenContractViewModel> TokensContracts { get; set; }
        public ObservableCollection<TezosTokenViewModel> Tokens { get; set; }
        public ObservableCollection<TezosTokenTransferViewModel> Transfers { get; set; }

        private TezosTokenContractViewModel _tokenContract;
        public TezosTokenContractViewModel TokenContract
        {
            get => _tokenContract;
            set
            {
                _tokenContract = value;
                TokenContractChanged(TokenContract);
            }
        }
        
        public bool HasTokenContract => TokenContract != null;
        public bool IsFa12 => TokenContract?.IsFa12 ?? false;
        public bool IsFa2 => TokenContract?.IsFa2 ?? false;
        public string TokenContractAddress => TokenContract?.Contract?.Address ?? "";
        public string TokenContractName => TokenContract?.Contract?.Name ?? "";
        public string TokenContractIconUrl => TokenContract?.IconUrl;
        
        public decimal Balance { get; set; }
        public string BalanceFormat { get; set; }
        public string BalanceCurrencyCode { get; set; }
        
        private readonly IAtomexApp _app;

        // private readonly IConversionViewModel _conversionViewModel;
        private bool _isBalanceUpdating;
        private CancellationTokenSource _cancellation;

        public TezosTokenStorage(AccountStorage accountStorage)
        {
            _app                 = accountStorage.AtomexApp ?? throw new ArgumentNullException(nameof(_app));
            // _conversionViewModel = conversionViewModel ?? throw new ArgumentNullException(nameof(conversionViewModel));

            SubscribeToUpdates();

            _ = ReloadTokenContractsAsync();
        }
        
        private void SubscribeToUpdates()
        {
            _app.AtomexClientChanged    += OnAtomexClientChanged;
            _app.Account.BalanceUpdated += OnBalanceUpdatedEventHandler;
        }
        
        private void OnAtomexClientChanged(object sender, AtomexClientChangedEventArgs e)
        {
            Tokens?.Clear();
            Transfers?.Clear();
            TokensContracts?.Clear();
            TokenContract = null;
        }
    }
}