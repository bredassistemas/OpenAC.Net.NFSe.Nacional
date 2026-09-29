using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenAC.Net.Core.Extensions;

namespace OpenAC.Net.NFSe.Nacional.Common.Model;

/// <summary>
/// Representa a resposta da consulta de uma nota por protocolo (ex.: SigISS).
/// </summary>
public sealed class RespostaConsultaProtocolo
{
    private NotaFiscalServico? nota;

    /// <summary>
    /// Situação da nota: em_processamento, rejeitado, recusada, erro_interno ou aprovado.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("protocolo")]
    public string Protocolo { get; set; } = string.Empty;

    /// <summary>
    /// XML da NFS-e no padrão do Sistema Nacional (preenchido quando aprovada).
    /// </summary>
    [JsonPropertyName("nfse")]
    public string XmlNFSe { get; set; } = string.Empty;

    /// <summary>
    /// Link da página de impressão do DANFSe (preenchido quando aprovada).
    /// </summary>
    [JsonPropertyName("linkImpressao")]
    public string LinkImpressao { get; set; } = string.Empty;

    /// <summary>
    /// Motivos da rejeição pelo próprio webservice (status "rejeitado").
    /// </summary>
    [JsonPropertyName("motivos")]
    public List<MotivoConsultaProtocolo> Motivos { get; set; } = new();

    /// <summary>
    /// Motivo da recusa pelo Ambiente Nacional ou da falha técnica (status "recusada" / "erro_interno").
    /// </summary>
    [JsonPropertyName("motivo")]
    public string Motivo { get; set; } = string.Empty;

    /// <summary>
    /// Demais campos do retorno não mapeados.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Outros { get; set; } = new();

    [JsonIgnore]
    public bool Aprovado => Status == "aprovado";

    [JsonIgnore]
    public bool EmProcessamento => Status == "em_processamento";
    /// <summary>
    /// Todos os motivos de rejeição/recusa em um único texto, para exibição ou log.
    /// </summary>
    [JsonIgnore]
    public string MensagemErro
    {
        get
        {
            var mensagens = new List<string>();
            foreach (var m in Motivos)
                mensagens.Add($"{m.Codigo} ({m.Campo}): {m.Mensagem}");
            if (!Motivo.IsEmpty())
                mensagens.Add(Motivo);
            return string.Join(" | ", mensagens);
        }
    }


    [JsonIgnore]
    public NotaFiscalServico? NFSe => XmlNFSe.IsEmpty() ? null : nota ??= NotaFiscalServico.Load(XmlNFSe);
}
/// <summary>
/// Motivo de rejeição retornado na consulta por protocolo (ex.: SigISS).
/// </summary>
public sealed class MotivoConsultaProtocolo
{
    [JsonPropertyName("codigo")]
    public string Codigo { get; set; } = string.Empty;

    [JsonPropertyName("mensagem")]
    public string Mensagem { get; set; } = string.Empty;

    [JsonPropertyName("campo")]
    public string Campo { get; set; } = string.Empty;
}