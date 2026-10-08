using System.Xml;
using System.Xml.Linq;
using Carimbo.Domain;
using Carimbo.GroundTruth;
using Xunit;

namespace Carimbo.Validation.Tests;

/// <summary>The mapping rules of docs/DANFE-MAPPING.md on minimal nfeProc documents (the same cases as the Python reader's tests).</summary>
public class NfeXmlMapperTests
{
    private const string Csosn102 = "<ICMSSN102><orig>0</orig><CSOSN>102</CSOSN></ICMSSN102>";

    private const string Icms00 =
        "<ICMS00><orig>0</orig><CST>00</CST><modBC>3</modBC><vBC>10.00</vBC><pICMS>18.00</pICMS><vICMS>1.80</vICMS></ICMS00>";

    [Fact]
    public void Crt_1_maps_the_csosn_into_cst_csosn()
    {
        var item = Map().Items.Single();

        Assert.Equal("0102", item.CstCsosn);
    }

    [Fact]
    public void Crt_3_maps_the_cst_and_the_icms_columns()
    {
        var item = Map(crt: "3", icms: Icms00).Items.Single();

        Assert.Equal("000", item.CstCsosn);
        Assert.Equal("10.00", item.IcmsBase.ToString());
        Assert.Equal("18.00", item.IcmsRate.ToString());
        Assert.Equal("1.80", item.IcmsAmount.ToString());
    }

    [Fact]
    public void An_absent_ipi_group_gives_zero_ipi_columns_and_a_present_one_is_read()
    {
        var absent = Map().Items.Single();
        Assert.Equal("0.00", absent.IpiRate.ToString());
        Assert.Equal("0.00", absent.IpiAmount.ToString());

        var present = Map(ipi: "<IPI><cEnq>999</cEnq><IPITrib><CST>50</CST><vBC>10.00</vBC><pIPI>5.00</pIPI><vIPI>0.50</vIPI></IPITrib></IPI>")
            .Items.Single();
        Assert.Equal("5.00", present.IpiRate.ToString());
        Assert.Equal("0.50", present.IpiAmount.ToString());
    }

    [Fact]
    public void Dest_cpf_gives_the_cpf_kind_and_dest_cnpj_the_cnpj_kind()
    {
        var person = Map(destId: "<CPF>52998224725</CPF>").Recipient;
        Assert.Equal("52998224725", person.TaxId);
        Assert.Equal(TaxIdKind.Cpf, person.TaxIdKind);

        Assert.Equal(TaxIdKind.Cnpj, Map().Recipient.TaxIdKind);
    }

    [Fact]
    public void An_absent_ie_is_null_and_isento_stays_isento()
    {
        var plain = Map();
        Assert.Null(plain.Recipient.Ie);
        Assert.Equal("ISENTO", plain.Issuer.Ie);

        var registered = Map(destIe: "<IE>123456789012</IE>", emitIe: string.Empty);
        Assert.Equal("123456789012", registered.Recipient.Ie);
        Assert.Null(registered.Issuer.Ie);

        Assert.Null(Map(destIe: "<IE> </IE>").Recipient.Ie);
    }

    [Fact]
    public void Quantity_is_always_written_with_four_decimals_half_up()
    {
        Assert.Equal("12.5000", Map(qCom: "12.5").Items.Single().Quantity.ToString());
        Assert.Equal("198.8210", Map(qCom: "198.821").Items.Single().Quantity.ToString());
        Assert.Equal("0.1235", Map(qCom: "0.12345").Items.Single().Quantity.ToString());
    }

    [Fact]
    public void The_issue_date_is_the_printed_local_date_never_converted_to_utc()
    {
        Assert.Equal(new DateOnly(2026, 3, 31), Map().IssueDate);
        Assert.Equal(new DateOnly(2026, 4, 1), Map(dhEmi: "2026-04-01T00:10:00+03:00").IssueDate);
    }

    [Fact]
    public void No_cobr_gives_an_empty_installment_list_and_dup_elements_are_read_in_order()
    {
        Assert.Empty(Map().Installments);

        var installments = Map(
            cobr: "<cobr><fat><nFat>1</nFat></fat>" +
                  "<dup><nDup>001</nDup><dVenc>2026-04-30</dVenc><vDup>4.00</vDup></dup>" +
                  "<dup><nDup>002</nDup><dVenc>2026-05-30</dVenc><vDup>6</vDup></dup></cobr>").Installments;

        Assert.Equal(["001", "002"], installments.Select(installment => installment.Number));
        Assert.Equal([new DateOnly(2026, 4, 30), new DateOnly(2026, 5, 30)], installments.Select(installment => installment.DueDate));
        Assert.Equal(["4.00", "6.00"], installments.Select(installment => installment.Amount.ToString()));
    }

    [Fact]
    public void Absent_total_tags_are_zero_and_the_access_key_loses_only_the_nfe_prefix()
    {
        var invoice = Map();

        Assert.Equal("35260311222333000181550010000001231000012346", invoice.AccessKey);
        Assert.Equal("0.00", invoice.Totals.Freight.ToString());
        Assert.Equal("0.00", invoice.Totals.IcmsStAmount.ToString());
        Assert.Equal("10.00", invoice.Totals.InvoiceTotal.ToString());
        Assert.Equal(123, invoice.Number);
        Assert.Equal(1, invoice.Series);
    }

    [Fact]
    public void A_document_without_nNF_throws_a_mapping_exception_naming_the_element()
    {
        var document = XDocument.Parse(Document().Replace("<nNF>123</nNF>", string.Empty, StringComparison.Ordinal));

        var error = Assert.Throws<NfeMappingException>(() => NfeXmlMapper.Map(document));

        Assert.Contains("nNF", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_required_element_and_a_malformed_decimal_are_named_too()
    {
        var blank = XDocument.Parse(Document().Replace("<xNome>EMITENTE</xNome>", "<xNome> </xNome>", StringComparison.Ordinal));
        Assert.Contains("emit/xNome", Assert.Throws<NfeMappingException>(() => NfeXmlMapper.Map(blank)).Message, StringComparison.Ordinal);

        var broken = XDocument.Parse(Document().Replace("<vProd>10.00</vProd></prod>", "<vProd>1O.00</vProd></prod>", StringComparison.Ordinal));
        Assert.Contains("vProd", Assert.Throws<NfeMappingException>(() => NfeXmlMapper.Map(broken)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_must_hold_exactly_one_icms_group()
    {
        var document = XDocument.Parse(Document(icms: Csosn102 + Csosn102));

        var error = Assert.Throws<NfeMappingException>(() => NfeXmlMapper.Map(document));

        Assert.Contains("imposto/ICMS", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_with_a_doctype_is_refused_and_nothing_is_resolved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"carimbo-doctype-{Guid.NewGuid():N}.xml");
        File.WriteAllText(
            path,
            "<?xml version=\"1.0\"?>\n" +
            "<!DOCTYPE NFe [<!ENTITY secret SYSTEM \"file:///etc/hostname\">]>\n" +
            "<NFe xmlns=\"http://www.portalfiscal.inf.br/nfe\"><infNFe Id=\"NFe1\">&secret;</infNFe></NFe>");
        try
        {
            Assert.Throws<XmlException>(() => NfeXmlMapper.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Invoice Map(
        string crt = "1",
        string icms = Csosn102,
        string destId = "<CNPJ>11222333000181</CNPJ>",
        string destIe = "",
        string emitIe = "<IE>ISENTO</IE>",
        string dhEmi = "2026-03-31T23:30:00-03:00",
        string ipi = "",
        string cobr = "",
        string qCom = "1.0000") =>
        NfeXmlMapper.Map(XDocument.Parse(Document(crt, icms, destId, destIe, emitIe, dhEmi, ipi, cobr, qCom)));

    private static string Document(
        string crt = "1",
        string icms = Csosn102,
        string destId = "<CNPJ>11222333000181</CNPJ>",
        string destIe = "",
        string emitIe = "<IE>ISENTO</IE>",
        string dhEmi = "2026-03-31T23:30:00-03:00",
        string ipi = "",
        string cobr = "",
        string qCom = "1.0000") =>
        $"""
        <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
        <NFe>
        <infNFe Id="NFe35260311222333000181550010000001231000012346" versao="4.00">
          <ide><natOp>VENDA</natOp><serie>1</serie><nNF>123</nNF><dhEmi>{dhEmi}</dhEmi></ide>
          <emit><CNPJ>11222333000181</CNPJ><xNome>EMITENTE</xNome>
            <enderEmit><UF>SP</UF></enderEmit>{emitIe}<CRT>{crt}</CRT></emit>
          <dest>{destId}<xNome>DESTINO</xNome><enderDest><UF>MG</UF></enderDest>{destIe}</dest>
          <det nItem="1">
            <prod><cProd>P1</cProd><xProd>PARAFUSO</xProd><NCM>73181500</NCM><CFOP>5102</CFOP>
              <uCom>UN</uCom><qCom>{qCom}</qCom><vUnCom>10.0000</vUnCom><vProd>10.00</vProd></prod>
            <imposto><ICMS>{icms}</ICMS>{ipi}</imposto>
          </det>
          <total><ICMSTot><vProd>10.00</vProd><vNF>10.00</vNF></ICMSTot></total>
          {cobr}
        </infNFe>
        </NFe>
        </nfeProc>
        """;
}
