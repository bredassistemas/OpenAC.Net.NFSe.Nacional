using System.Diagnostics;
using System.Net;
using OpenAC.Net.DFe.Core.Common;
using OpenAC.Net.DFe.Core.Extensions;
using OpenAC.Net.NFSe.Nacional.Common.Model;
using OpenAC.Net.NFSe.Nacional.Common.Types;

namespace OpenAC.Net.NFSe.Nacional.Test;

/// <summary>
/// Testes de emissão em homologação no webservice próprio da SIGCORP (Marília/SP).
/// Preencha as constantes abaixo antes de executar (não faça commit de credenciais).
/// Fluxo: EnviarAsync(dps, numeroNFSe) devolve o protocolo; a situação da nota
/// (em_processamento, rejeitado, recusada, erro_interno, aprovado) vem de ConsultarProtocoloAsync.
/// </summary>
[TestClass]
public class TestEmissaoSigISSMarilia
{
    #region Dados de configuração (preencher localmente)

    private const string CodMunIBGE = ""; // Marília/SP

    private const string InscricaoMunicipal = ""; // usuário do webservice
    private const string Senha = "";
    private const string CnpjPrestador = "";

    // A plataforma exige nome e endereço nacional do prestador na DPS (conforme cadastro do CNPJ).
    private const string RazaoSocialPrestador = "";
    private const string LogradouroPrestador = "";
    private const string NumeroPrestador = "";
    private const string BairroPrestador = "";
    private const string CepPrestador = "";

    // Serviço autorizado para o prestador no cadastro municipal.
    private const string CodTributacaoNacional = "010401";
    private const string CodNBS = "115021000";

    // Número da nota definido pelo prestador (cabeçalho X-NFSe-nNFSe). Não pode repetir.
    private const long NumeroNFSe = 47548;

    // Série e número da DPS (RPS), de uso interno do prestador.
    private const string SerieDps = "1";
    private const string NumeroDps = "47548";

    // Protocolo retornado pelo envio, para o teste de consulta.
    private const string Protocolo = "";

    #endregion Dados de configuração

    [TestMethod]
    public async Task EmissaoNFSeMarilia()
    {
        var openNFSeNacional = CriarOpenNFSeNacional();

        var dps = CriarDps(openNFSeNacional, ibscbs: null);

        var retorno = await openNFSeNacional.EnviarAsync(dps, NumeroNFSe);

        Debug.WriteLine(retorno.XmlEnvio);
        Debug.WriteLine(retorno.JsonRetorno);

        Assert.IsTrue(retorno.Sucesso, "Erro no envio: " + retorno.JsonRetorno);
        Assert.IsFalse(string.IsNullOrEmpty(retorno.Resultado?.Protocolo), "Protocolo não retornado: " + retorno.JsonRetorno);

        Console.WriteLine($"Protocolo: {retorno.Resultado!.Protocolo}");
    }

    /// <summary>
    /// Emissão com o grupo IBSCBS. Não use prestador do Simples Nacional (a nota é rejeitada),
    /// e o cNBS passa a ser obrigatório. Os códigos abaixo são exemplos: a combinação
    /// cClassTrib + NBS + cIndOp + cTribNac precisa existir na tabela do município.
    /// </summary>
    [TestMethod]
    public async Task EmissaoNFSeMariliaComIBSCBS()
    {
        var openNFSeNacional = CriarOpenNFSeNacional();

        var ibscbs = new RTCInfoIBSCBS
        {
            FinalidadeNFSe = RTCFinNFSe.Regular,
            IndicadorUsoFinal = RTCIndFinal.Nao,
            CodigoIndicadorOperacao = "100301",
            IndicadorDestinatario = RTCIndDest.ProprioTomador,
            Valores = new RTCInfoValoresIBSCBS
            {
                Tributos = new RTCInfoTributosIBSCBS
                {
                    GrupoIBSCBS = new RTCInfoTributosSitClas
                    {
                        CodigoSituacaoTributaria = "000",
                        CodigoClassificacaoTributaria = "000001"
                    }
                }
            }
        };

        var dps = CriarDps(openNFSeNacional, ibscbs);

        var retorno = await openNFSeNacional.EnviarAsync(dps, NumeroNFSe);

        Debug.WriteLine(retorno.XmlEnvio);
        Debug.WriteLine(retorno.JsonRetorno);

        Assert.IsTrue(retorno.Sucesso, "Erro no envio: " + retorno.JsonRetorno);
        Console.WriteLine($"Protocolo: {retorno.Resultado?.Protocolo}");
    }

    [TestMethod]
    public async Task ConsultarProtocoloMarilia()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(Protocolo), "Preencha a constante Protocolo com o retorno do envio.");

        var openNFSeNacional = CriarOpenNFSeNacional();

        // A nota fica em_processamento enquanto é validada e enviada ao Ambiente Nacional.
        var retorno = await openNFSeNacional.ConsultarProtocoloAsync(Protocolo);
        for (var tentativa = 1; tentativa < 10 && retorno.Sucesso && retorno.Resultado?.EmProcessamento == true; tentativa++)
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            retorno = await openNFSeNacional.ConsultarProtocoloAsync(Protocolo);
        }

        Debug.WriteLine(retorno.JsonRetorno);
        Console.WriteLine(retorno.JsonRetorno);

        Assert.IsTrue(retorno.Sucesso, "Erro na consulta: " + retorno.JsonRetorno);
        Assert.IsTrue(retorno.Resultado?.Aprovado == true,
            $"Status: {retorno.Resultado?.Status} | {retorno.Resultado?.MensagemErro}");

        Console.WriteLine($"Aprovada. Link: {retorno.Resultado!.LinkImpressao}");
    }
    

    private static OpenNFSeNacional CriarOpenNFSeNacional()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(InscricaoMunicipal) || string.IsNullOrWhiteSpace(Senha) ||
                       string.IsNullOrWhiteSpace(CnpjPrestador),
            "Preencha InscricaoMunicipal, Senha e CnpjPrestador em TestEmissaoSigISSMarilia.");

        var openNFSeNacional = new OpenNFSeNacional();
        var config = openNFSeNacional.Configuracoes;

        config.Geral.Versao = VersaoNFSe.Ve101;
        config.Geral.Salvar = true;
        config.Geral.RetirarAcentos = true;
        config.Arquivos.Salvar = true;
        config.Arquivos.PathSalvar = Path.Combine(Path.GetTempPath(), "NFSeMarilia");
        config.Arquivos.PathSchemas = Path.Combine(AppContext.BaseDirectory, "Schemas", VersaoNFSe.Ve101.GetDFeValue());

        config.WebServices.Ambiente = DFeTipoAmbiente.Homologacao;
        config.WebServices.Protocolos = SecurityProtocolType.Tls12;
        config.WebServices.CodigoMunicipio = int.Parse(CodMunIBGE);
        config.WebServices.InscricaoMunicipal = InscricaoMunicipal;
        config.WebServices.Senha = Senha;
        config.WebServices.CnpjPrestador = CnpjPrestador;

        Console.WriteLine($"Arquivos salvos em: {config.Arquivos.PathSalvar}");

        return openNFSeNacional;
    }

    private static Dps CriarDps(OpenNFSeNacional openNFSeNacional, RTCInfoIBSCBS? ibscbs)
    {
        var prest = new PrestadorDps
        {
            CNPJ = CnpjPrestador,
            InscricaoMunicipal = InscricaoMunicipal,
            Nome = RazaoSocialPrestador,
            Endereco = new EnderecoNFSe
            {
                Logradouro = LogradouroPrestador,
                Numero = NumeroPrestador,
                Bairro = BairroPrestador,
                Municipio = new MunicipioNacional
                {
                    CodMunicipio = CodMunIBGE,
                    CEP = CepPrestador
                }
            },
            Regime = new RegimeTributario
            {
                // Simples Nacional exige pAliq (2% a 5%) e não pode enviar IBSCBS.
                OptanteSimplesNacional = OptanteSimplesNacional.NaoOptante,
                RegimeEspecial = RegimeEspecial.Nenhum
            }
        };
        var tomador = new InfoPessoaNFSe
        {
            CPF = "",                // CPF válido de teste (só números)
            Nome = "",
            
            Endereco = new EnderecoNFSe
            {
                Logradouro = "",
                Numero = "",
                Bairro = "",
                Municipio = new MunicipioNacional
                {
                    CodMunicipio = "",
                    CEP = ""
                }
            }
        };

        var serv = new ServicoNFSe
        {
            Localidade = new LocalidadeNFSe
            {
                CodMunicipioPrestacao = CodMunIBGE,
                CodPaisPrestacao = null
            },
            Informacoes = new InformacoesServico
            {
                // Ajuste para um código de serviço que o prestador pode emitir.
                CodTributacaoNacional = CodTributacaoNacional,
                CodNBS = CodNBS,
                Descricao = "Servico teste homologacao"
            }
        };

        var valores = new ValoresDps
        {
            ValoresServico = new ValoresServico
            {
                Valor = 1
            },
            Tributos = new TributosNFSe
            {
                Municipal = new TributoMunicipal
                {
                    ISSQN = TributoISSQN.OperacaoTributavel,
                    TipoRetencaoISSQN = TipoRetencaoISSQN.NaoRetido,
                },
                Total = new TotalTributos
                {
                    PorcentagemTotal = new PorcentagemTotalTributos
                    {
                        TotalEstadual = 0,
                        TotalFederal = 0,
                        TotalMunicipal = 0,
                    }
                }
            }
        };

        return new Dps
        {
            Versao = openNFSeNacional.Configuracoes.Geral.Versao,
            Informacoes = new InfDps
            {
                // O Id é montado automaticamente no envio quando vazio.
                TipoAmbiente = DFeTipoAmbiente.Homologacao,
                DhEmissao = DateTime.Now,
                LocalidadeEmitente = CodMunIBGE,
                
                Serie = SerieDps,
                NumeroDps = NumeroDps,
                Competencia = DateTime.Now,
                TipoEmitente = EmitenteDps.Prestador,
                Prestador = prest,
                Tomador = tomador,
                Servico = serv,
                Valores = valores,
                IBSCBS = ibscbs,
            }
        };
    }
}