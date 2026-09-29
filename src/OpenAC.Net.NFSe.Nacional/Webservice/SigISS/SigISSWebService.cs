// Webservice próprio da SIGCORP (meumunicipio.online) que recebe a DPS no
// padrão do Sistema Nacional NFS-e. Particularidades (manual complementar):
//  - Autenticação HTTP Basic (IM:senha), sem certificado digital;
//  - Cabeçalhos X-NFSe-Prestador-CNPJ e X-NFSe-nNFSe (número da nota informado pelo prestador);
//  - Corpo da recepção é o XML da DPS (application/xml), retorno 202 { "protocolo": "..." };
//  - Consulta assíncrona por protocolo: GET /consulta/{protocolo}.

using OpenAC.Net.Core.Extensions;
using OpenAC.Net.Core.Logging;
using OpenAC.Net.DFe.Core.Common;
using OpenAC.Net.DFe.Core.Document;
using OpenAC.Net.DFe.Core.Serializer;
using OpenAC.Net.NFSe.Nacional.Common;
using OpenAC.Net.NFSe.Nacional.Common.Model;
using OpenAC.Net.NFSe.Nacional.Common.Types;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Linq;

namespace OpenAC.Net.NFSe.Nacional.Webservice.SigISS;

/// <summary>
/// Classe de serviço web para integração com o webservice próprio da SIGCORP (ex.: Marília/SP).
/// </summary>
public class SigISSWebService : NFSeWebserviceBase
{
    #region Fields

    private const string HeaderCnpjPrestador = "X-NFSe-Prestador-CNPJ";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    #endregion Fields

    #region Constructors

    public SigISSWebService(ConfiguracaoNFSe configuracaoNFSe, NFSeServiceInfo serviceInfo) :
        base(configuracaoNFSe, serviceInfo)
    {
    }

    #endregion Constructors

    #region Methods

    /// <summary>
    /// O webservice exige o número da NFS-e (cabeçalho X-NFSe-nNFSe).
    /// Utilize <see cref="EnviarAsync(Dps, long)"/>.
    /// </summary>
    public override Task<NFSeResponse<RespostaEnvioDps>> EnviarAsync(Dps dps) =>
        throw new InvalidOperationException("O provedor SigISS exige o número da NFS-e (nNFSe). Utilize EnviarAsync(dps, numeroNFSe).");

    /// <summary>
    /// Envia a DPS (sem assinatura) para recepção. O retorno contém apenas o protocolo;
    /// a situação da nota deve ser obtida com <see cref="ConsultarProtocoloAsync"/>.
    /// </summary>
    public override async Task<NFSeResponse<RespostaEnvioDps>> EnviarAsync(Dps dps, long numeroNFSe)
    {
        if (numeroNFSe <= 0 || numeroNFSe > 9999999999999)
            throw new ArgumentOutOfRangeException(nameof(numeroNFSe), "O número da NFS-e deve ter de 1 a 13 dígitos.");

        ValidarPrestador(dps.Informacoes.Prestador);

        var xmlDps = GerarXmlSemAssinatura(dps);

        ValidarSchema(SchemaNFSe.DPS, xmlDps, dps.Versao);

        // Feito após a validação, pois o schema nacional não aceita os dois campos em locPrest.
        xmlDps = IncluirPaisPrestacaoBrasil(xmlDps);

        var documento = dps.Informacoes.Prestador.CPF ?? dps.Informacoes.Prestador.CNPJ;

        var prefixoNomeArquivo = Configuracao.Arquivos.PadronizarNomes
            ? dps.Informacoes.Id
            : dps.Informacoes.NumeroDps.ZeroFill(6);

        GravarDpsEmDisco(xmlDps, $"{prefixoNomeArquivo}_dps.xml", documento, dps.Informacoes.DhEmissao.DateTime);

        this.Log().Debug($"SigISS: [Enviar][Envio] - {xmlDps}");

        // O webservice só aceita application/xml ou text/xml, por isso o Content-Type é
        // montado sem o parâmetro charset que o StringContent acrescenta por padrão.
        var content = new StringContent(xmlDps, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");

        var url = ObterUrlBase();
        var headers = new List<KeyValuePair<string, string>>
        {
            // O CNPJ do cabeçalho deve ser o mesmo do prestador identificado na DPS.
            new(HeaderCnpjPrestador, (documento ?? string.Empty).OnlyNumbers()),
            new("X-NFSe-nNFSe", numeroNFSe.ToString())
        };

        var httpResponse = await SendAsync(content, HttpMethod.Post, $"{url}/recepcao", headers);
        var strResponse = await LerResposta(httpResponse);

        this.Log().Debug($"SigISS: [Enviar][Resposta] - {strResponse}");

        GravarArquivoEmDisco(strResponse, $"Enviar-{prefixoNomeArquivo}-resp.json", documento);

        return NFSeResponse<RespostaEnvioDps>.Create(xmlDps, xmlDps, strResponse, httpResponse.IsSuccessStatusCode, JsonOptions);
    }

    /// <summary>
    /// Consulta a situação de uma nota enviada, a partir do protocolo retornado na recepção.
    /// </summary>
    public override async Task<NFSeResponse<RespostaConsultaProtocolo>> ConsultarProtocoloAsync(string protocolo)
    {
        if (protocolo.IsEmpty())
            throw new ArgumentException("Protocolo não informado.", nameof(protocolo));

        this.Log().Debug($"SigISS: [ConsultarProtocolo][Envio] - {protocolo}");

        var url = ObterUrlBase();
        var httpResponse = await SendAsync(null, HttpMethod.Get, $"{url}/consulta/{Uri.EscapeDataString(protocolo)}");
        var strResponse = await LerResposta(httpResponse);

        this.Log().Debug($"SigISS: [ConsultarProtocolo][Resposta] - {strResponse}");

        var documento = Configuracao.WebServices.CnpjPrestador;
        GravarArquivoEmDisco(strResponse, $"ConsultarProtocolo-{protocolo}-resp.json", documento);

        var retorno = NFSeResponse<RespostaConsultaProtocolo>.Create("", "", strResponse, httpResponse.IsSuccessStatusCode, JsonOptions);

        if (retorno.Sucesso && retorno.Resultado?.Aprovado == true && !retorno.Resultado.XmlNFSe.IsEmpty())
            GravarNFSeEmDisco(retorno.Resultado.XmlNFSe, $"{protocolo}_nfse.xml", documento, DateTime.Now);

        return retorno;
    }

    /// <summary>
    /// Envia a requisição sem certificado digital, com autenticação Basic (IM:senha)
    /// e o cabeçalho X-NFSe-Prestador-CNPJ, exigidos em todas as chamadas.
    /// </summary>
    protected override async Task<HttpResponseMessage> SendAsync(HttpContent? content, HttpMethod method, string url,
        IEnumerable<KeyValuePair<string, string>>? headers = null)
    {
        var usuario = Configuracao.WebServices.InscricaoMunicipal;
        var senha = Configuracao.WebServices.Senha;

        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(senha))
            throw new InvalidOperationException("Informe WebServices.InscricaoMunicipal (usuário) e WebServices.Senha.");

        var listaHeaders = headers != null
            ? new List<KeyValuePair<string, string>>(headers)
            : new List<KeyValuePair<string, string>>();

        if (!listaHeaders.Exists(x => x.Key == HeaderCnpjPrestador))
        {
            var cnpj = (Configuracao.WebServices.CnpjPrestador ?? string.Empty).OnlyNumbers();
            if (cnpj.IsEmpty())
                throw new InvalidOperationException($"Informe WebServices.CnpjPrestador (cabeçalho {HeaderCnpjPrestador}).");

            listaHeaders.Add(new KeyValuePair<string, string>(HeaderCnpjPrestador, cnpj));
        }

        var handler = new HttpClientHandler();
        var client = new HttpClient(handler);

        var request = new HttpRequestMessage(method, url);

        var assemblyName = GetType().Assembly.GetName();
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("OpenAC.Net.NFSe.Nacional", assemblyName!.Version!.ToString()));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/OpenAC-Net/OpenAC.Net.NFSe.Nacional)"));

        var credenciais = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{usuario!.Trim()}:{senha!.Trim()}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credenciais);

        foreach (var header in listaHeaders)
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);

        request.Content = content;

        return await client.SendAsync(request);
    }

    public override Task<byte[]> DownloadDANFSeAsync(string chave) =>
        throw OperacaoNaoSuportada(nameof(DownloadDANFSeAsync) + " (utilize o linkImpressao retornado na consulta por protocolo)");

    public override Task<NFSeResponse<RespostaConsultaDFe>> ConsultaNsuAsync(int nsu) =>
        throw OperacaoNaoSuportada(nameof(ConsultaNsuAsync));

    public override Task<NFSeResponse<RespostaConsultaDFe>> ConsultaChaveAsync(string chave) =>
        throw OperacaoNaoSuportada(nameof(ConsultaChaveAsync));

    public override Task<NFSeResponse<RespostaConsultaChaveDps>> ConsultaChaveDpsAsync(string id) =>
        throw OperacaoNaoSuportada(nameof(ConsultaChaveDpsAsync));

    public override Task<bool> ConsultaExisteDpsAsync(string id) =>
        throw OperacaoNaoSuportada(nameof(ConsultaExisteDpsAsync));

    public override Task<NFSeResponse<RespostaEnvioEvento>> EnviarEventoAsync(PedidoRegistroEvento evento) =>
        throw OperacaoNaoSuportada(nameof(EnviarEventoAsync));

    /// <summary>
    /// Gera o XML da DPS sem a assinatura digital (o webservice autentica por login e senha).
    /// Monta o Id da DPS quando não informado, no mesmo formato do padrão nacional.
    /// </summary>
    private string GerarXmlSemAssinatura(Dps dps)
    {
        var inf = dps.Informacoes;
        if (inf.Id.IsEmpty())
        {
            var tipo = inf.Prestador.CNPJ.IsEmpty() ? "1" : "2";
            var documento = inf.Prestador.CNPJ.IsEmpty() ? inf.Prestador.CPF : inf.Prestador.CNPJ;

            inf.Id = $"DPS{inf.LocalidadeEmitente.ZeroFill(7)}{tipo}{documento?.OnlyNumbers().ZeroFill(14)}" +
                     $"{inf.Serie.ZeroFill(5)}{inf.NumeroDps.ZeroFill(15)}";
        }

        // Assinatura vazia não é serializada (o grupo Signature é opcional no schema da DPS).
        dps.Signature = new DFeSignature();

        var options = DFeSaveOptions.DisableFormatting;
        if (Configuracao.Geral.RetirarAcentos)
            options |= DFeSaveOptions.RemoveAccents;

        return dps.GetXml(options);
    }

    /// <summary>
    /// Exigência da plataforma (orientação do suporte SIGCORP): serviço prestado no Brasil deve
    /// informar cPaisPrestacao = BR junto com cLocPrestacao. Sem ele a recepção devolve E0304.
    /// A plataforma trata o campo antes de gerar a NFS-e (incidência segue o cLocPrestacao).
    /// </summary>
    private static string IncluirPaisPrestacaoBrasil(string xmlDps)
    {
        var doc = XDocument.Parse(xmlDps, LoadOptions.PreserveWhitespace);
        var locPrest = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "locPrest");
        var cLocPrestacao = locPrest?.Elements().FirstOrDefault(x => x.Name.LocalName == "cLocPrestacao");

        if (cLocPrestacao == null || locPrest!.Elements().Any(x => x.Name.LocalName == "cPaisPrestacao"))
            return xmlDps;

        cLocPrestacao.AddAfterSelf(new XElement(cLocPrestacao.Name.Namespace + "cPaisPrestacao", "BR"));

        return doc.Declaration + doc.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Diferente do padrão nacional (que busca os dados no CNC), esta plataforma exige na DPS
    /// o nome empresarial (xNome) e o endereço nacional (end/endNac) do prestador. Sem o endereço
    /// a recepção devolve E0304 (código de país), e sem o nome a nota é rejeitada.
    /// </summary>
    private static void ValidarPrestador(PrestadorDps prestador)
    {
        if (string.IsNullOrWhiteSpace(prestador.Nome))
            throw new InvalidOperationException("SigISS: informe o nome empresarial do prestador (Prestador.Nome / prest/xNome).");

        var endereco = prestador.Endereco;
        if (endereco?.Municipio is not MunicipioNacional municipio || municipio.CodMunicipio.IsEmpty() ||
            municipio.CEP.IsEmpty() || endereco.Logradouro.IsEmpty() || endereco.Numero.IsEmpty() || endereco.Bairro.IsEmpty())
            throw new InvalidOperationException(
                "SigISS: informe o endereço nacional completo do prestador (Prestador.Endereco com MunicipioNacional, CEP, logradouro, número e bairro).");
    }

    private string ObterUrlBase()
    {
        var url = ServiceInfo[Configuracao.WebServices.Ambiente][TipoUrl.Enviar];
        if (url.IsEmpty())
            throw new InvalidOperationException("URL do webservice não encontrada na configuração do município.");

        return url!.TrimEnd('/');
    }

    /// <summary>
    /// Lê o corpo da resposta. As respostas 401/403 vêm sem corpo; nesse caso devolve um JSON
    /// com o código HTTP para que o motivo fique visível no retorno.
    /// </summary>
    private static async Task<string> LerResposta(HttpResponseMessage httpResponse)
    {
        var strResponse = await httpResponse.Content.ReadAsStringAsync();
        if (!strResponse.IsEmpty()) return strResponse;

        var status = (int)httpResponse.StatusCode;
        var motivo = status switch
        {
            401 => "Falha de autenticação (IM/senha/CNPJ).",
            403 => "IM do cabeçalho de autenticação diverge do IM do prestador na DPS.",
            _ => httpResponse.ReasonPhrase ?? string.Empty
        };

        return JsonSerializer.Serialize(new { statusCode = status, message = motivo });
    }

    #endregion Methods
}