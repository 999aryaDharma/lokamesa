using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace DocumentIDE.GoldenTests;

/// <summary>
/// Deterministic programmatic DOCX package builder for the QA-01 golden corpus.
/// Every entry is written in a fixed order with a fixed ZIP timestamp, so the
/// produced bytes are identical across machines and runs (asserted by the
/// corpus determinism test). No opaque binary fixtures are committed.
/// </summary>
internal static class GoldenDocxFixtureBuilder
{
    public static readonly DateTimeOffset DeterministicTimestamp = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string MainDocumentContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
    private const string HeaderContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml";
    private const string FooterContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml";
    private const string StylesContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml";
    private const string NumberingContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml";
    private const string HeaderRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header";
    private const string FooterRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer";
    private const string ImageRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    private const string HyperlinkRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
    private const string StylesRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
    private const string NumberingRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering";

    // 1x1 transparent PNG — fixed bytes so the images fixture stays deterministic.
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    // ------------------------------------------------------------------
    // Valid golden fixtures
    // ------------------------------------------------------------------

    public static byte[] Simple()
    {
        const string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <w:body>
                    <w:p>
                        <w:r><w:t>Hello Golden Corpus Document</w:t></w:r>
                    </w:p>
                    <w:p>
                        <w:r><w:t>Second paragraph stays untouched.</w:t></w:r>
                    </w:p>
                    <w:sectPr>
                        <w:pgSz w:w="12240" w:h="15840"/>
                        <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>
                    </w:sectPr>
                </w:body>
            </w:document>
            """;

        return Package(ContentTypes(MainDocumentContentType), documentXml);
    }

    public static byte[] SplitRuns()
    {
        const string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <w:body>
                    <w:p>
                        <w:r><w:t xml:space="preserve">Project </w:t></w:r>
                        <w:r><w:rPr><w:b/><w:rFonts w:ascii="Arial"/></w:rPr><w:t>NUSA-</w:t></w:r>
                        <w:r><w:rPr><w:i/><w:sz w:val="24"/></w:rPr><w:t>DUA-1</w:t></w:r>
                        <w:r><w:t xml:space="preserve"> is active.</w:t></w:r>
                    </w:p>
                    <w:sectPr>
                        <w:pgSz w:w="12240" w:h="15840"/>
                        <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>
                    </w:sectPr>
                </w:body>
            </w:document>
            """;

        return Package(ContentTypes(MainDocumentContentType), documentXml);
    }

    public static byte[] MixedFormat()
    {
        const string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <w:body>
                    <w:p>
                        <w:pPr><w:jc w:val="center"/><w:spacing w:after="240"/></w:pPr>
                        <w:r><w:rPr><w:b/><w:rFonts w:ascii="Georgia" w:hAnsi="Georgia"/><w:sz w:val="32"/></w:rPr><w:t xml:space="preserve">Quarterly </w:t></w:r>
                        <w:r><w:rPr><w:i/><w:rFonts w:ascii="Georgia" w:hAnsi="Georgia"/><w:sz w:val="32"/></w:rPr><w:t>Report</w:t></w:r>
                        <w:r><w:rPr><w:u w:val="single"/><w:rFonts w:ascii="Georgia" w:hAnsi="Georgia"/><w:sz w:val="32"/></w:rPr><w:t xml:space="preserve"> Summary</w:t></w:r>
                        <w:r><w:rPr><w:color w:val="C00000"/><w:rFonts w:ascii="Georgia" w:hAnsi="Georgia"/><w:sz w:val="32"/></w:rPr><w:t xml:space="preserve"> — GOLDEN</w:t></w:r>
                    </w:p>
                    <w:p>
                        <w:r><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri"/><w:sz w:val="22"/></w:rPr><w:t>Unrelated paragraph with different formatting.</w:t></w:r>
                    </w:p>
                    <w:sectPr>
                        <w:pgSz w:w="12240" w:h="15840"/>
                        <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>
                    </w:sectPr>
                </w:body>
            </w:document>
            """;

        return Package(ContentTypes(MainDocumentContentType), documentXml);
    }

    public static byte[] Tables()
    {
        const string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <w:body>
                    <w:p><w:r><w:t>Body paragraph before tables.</w:t></w:r></w:p>
                    <w:tbl>
                        <w:tblPr>
                            <w:tblW w:w="0" w:type="auto"/>
                            <w:tblBorders>
                                <w:top w:val="single" w:sz="4" w:color="auto"/>
                                <w:left w:val="single" w:sz="4" w:color="auto"/>
                                <w:bottom w:val="single" w:sz="4" w:color="auto"/>
                                <w:right w:val="single" w:sz="4" w:color="auto"/>
                            </w:tblBorders>
                        </w:tblPr>
                        <w:tblGrid>
                            <w:gridCol w:w="3000"/>
                            <w:gridCol w:w="3000"/>
                        </w:tblGrid>
                        <w:tr>
                            <w:tc>
                                <w:tcPr><w:tcW w:w="3000" w:type="dxa"/></w:tcPr>
                                <w:p><w:r><w:t>Alpha GOLDEN-TARGET cell</w:t></w:r></w:p>
                            </w:tc>
                            <w:tc>
                                <w:tcPr><w:tcW w:w="3000" w:type="dxa"/></w:tcPr>
                                <w:p><w:r><w:t>Beta cell</w:t></w:r></w:p>
                            </w:tc>
                        </w:tr>
                        <w:tr>
                            <w:tc><w:p><w:r><w:t>Gamma</w:t></w:r></w:p></w:tc>
                            <w:tc><w:p><w:r><w:t>Delta</w:t></w:r></w:p></w:tc>
                        </w:tr>
                    </w:tbl>
                    <w:p><w:r><w:t>Body paragraph after table.</w:t></w:r></w:p>
                    <w:sectPr>
                        <w:pgSz w:w="12240" w:h="15840"/>
                        <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>
                    </w:sectPr>
                </w:body>
            </w:document>
            """;

        return Package(ContentTypes(MainDocumentContentType), documentXml);
    }

    public static byte[] Headers()
    {
        const string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <w:body>
                    <w:p><w:r><w:t>Main body with no marker.</w:t></w:r></w:p>
                    <w:sectPr>
                        <w:headerReference w:type="default" r:id="rIdHeader1"/>
                        <w:headerReference w:type="even" r:id="rIdHeader2"/>
                        <w:pgSz w:w="12240" w:h="15840"/>
                        <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>
                    </w:sectPr>
                </w:body>
            </w:document>
            """;

        const string header1Xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:p><w:r><w:t>ACME GOLDEN Confidential</w:t></w:r></w:p>
            </w:hdr>
            """;

        const string header2Xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:p><w:r><w:t>Even page header</w:t></w:r></w:p>
            </w:hdr>
            """;

        string documentRels = RelsXml(
            ("rIdHeader1", HeaderRelType, "header1.xml", null),
            ("rIdHeader2", HeaderRelType, "header2.xml", null));

        return Package(
            ContentTypes(MainDocumentContentType,
                ("/word/header1.xml", HeaderContentType),
                ("/word/header2.xml", HeaderContentType)),
            documentXml,
            documentRels,
            extraXmlParts: new[]
            {
                ("word/header1.xml", header1Xml),
                ("word/header2.xml", header2Xml)
            });
    }

    public static byte[] Footers()
    {
        const string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <w:body>
                    <w:p><w:r><w:t>Main body with footer wiring.</w:t></w:r></w:p>
                    <w:sectPr>
                        <w:footerReference w:type="default" r:id="rIdFooter1"/>
                        <w:footerReference w:type="even" r:id="rIdFooter2"/>
                        <w:pgSz w:w="12240" w:h="15840"/>
                        <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>
                    </w:sectPr>
                </w:body>
            </w:document>
            """;

        const string footer1Xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:p><w:r><w:t>Golden Draft - internal</w:t></w:r></w:p>
            </w:ftr>
            """;

        const string footer2Xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:p><w:r><w:t>Strictly private</w:t></w:r></w:p>
            </w:ftr>
            """;

        string documentRels = RelsXml(
            ("rIdFooter1", FooterRelType, "footer1.xml", null),
            ("rIdFooter2", FooterRelType, "footer2.xml", null));

        return Package(
            ContentTypes(MainDocumentContentType,
                ("/word/footer1.xml", FooterContentType),
                ("/word/footer2.xml", FooterContentType)),
            documentXml,
            documentRels,
            extraXmlParts: new[]
            {
                ("word/footer1.xml", footer1Xml),
                ("word/footer2.xml", footer2Xml)
            });
    }

    public static byte[] Sections()
    {
        const string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <w:body>
                    <w:p>
                        <w:pPr>
                            <w:sectPr>
                                <w:pgSz w:w="12240" w:h="15840"/>
                                <w:pgMar w:top="1440" w:right="1800" w:bottom="1440" w:left="1800"/>
                            </w:sectPr>
                        </w:pPr>
                        <w:r><w:t>Section one content GOLDEN-ONE.</w:t></w:r>
                    </w:p>
                    <w:p>
                        <w:r><w:t>Section two content GOLDEN-TWO.</w:t></w:r>
                    </w:p>
                    <w:sectPr>
                        <w:pgSz w:w="15840" w:h="12240" w:orient="landscape"/>
                        <w:pgMar w:top="720" w:right="720" w:bottom="720" w:left="720"/>
                    </w:sectPr>
                </w:body>
            </w:document>
            """;

        return Package(ContentTypes(MainDocumentContentType), documentXml);
    }

    public static byte[] Images()
    {
        const string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
                <w:body>
                    <w:p><w:r><w:t>Figure caption GOLDEN image.</w:t></w:r></w:p>
                    <w:p>
                        <w:r>
                            <w:drawing>
                                <wp:inline distT="0" distB="0" distL="0" distR="0">
                                    <wp:extent cx="914400" cy="914400"/>
                                    <wp:docPr id="1" name="Picture 1"/>
                                    <a:graphic>
                                        <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                                            <pic:pic>
                                                <pic:nvPicPr>
                                                    <pic:cNvPr id="0" name="image1.png"/>
                                                    <pic:cNvPicPr/>
                                                </pic:nvPicPr>
                                                <pic:blipFill>
                                                    <a:blip r:embed="rIdImage"/>
                                                    <a:stretch><a:fillRect/></a:stretch>
                                                </pic:blipFill>
                                                <pic:spPr>
                                                    <a:xfrm>
                                                        <a:off x="0" y="0"/>
                                                        <a:ext cx="914400" cy="914400"/>
                                                    </a:xfrm>
                                                    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                                                </pic:spPr>
                                            </pic:pic>
                                        </a:graphicData>
                                    </a:graphic>
                                </wp:inline>
                            </w:drawing>
                        </w:r>
                    </w:p>
                    <w:sectPr>
                        <w:pgSz w:w="12240" w:h="15840"/>
                        <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>
                    </w:sectPr>
                </w:body>
            </w:document>
            """;

        string documentRels = RelsXml(("rIdImage", ImageRelType, "media/image1.png", null));

        return Package(
            ContentTypes(MainDocumentContentType, includePng: true),
            documentXml,
            documentRels,
            extraBinaryParts: new[] { ("word/media/image1.png", OnePixelPng) });
    }

    public static byte[] Hyperlinks()
    {
        const string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <w:body>
                    <w:p>
                        <w:r><w:t xml:space="preserve">Visit </w:t></w:r>
                        <w:hyperlink r:id="rIdHyperlink" w:history="1">
                            <w:r><w:rPr><w:rStyle w:val="Hyperlink"/></w:rPr><w:t>Example Site</w:t></w:r>
                        </w:hyperlink>
                        <w:r><w:t xml:space="preserve"> GOLDEN link.</w:t></w:r>
                    </w:p>
                    <w:sectPr>
                        <w:pgSz w:w="12240" w:h="15840"/>
                        <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>
                    </w:sectPr>
                </w:body>
            </w:document>
            """;

        const string stylesXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:style w:type="character" w:styleId="Hyperlink">
                    <w:name w:val="Hyperlink"/>
                    <w:rPr><w:color w:val="0563C1"/><w:u w:val="single"/></w:rPr>
                </w:style>
            </w:styles>
            """;

        string documentRels = RelsXml(
            ("rIdHyperlink", HyperlinkRelType, "https://example.invalid/golden", "External"),
            ("rIdStyles", StylesRelType, "styles.xml", null));

        return Package(
            ContentTypes(MainDocumentContentType, ("/word/styles.xml", StylesContentType)),
            documentXml,
            documentRels,
            extraXmlParts: new[] { ("word/styles.xml", stylesXml) });
    }

    public static byte[] Numbering()
    {
        const string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <w:body>
                    <w:p>
                        <w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr>
                        <w:r><w:t>Item one GOLDEN.</w:t></w:r>
                    </w:p>
                    <w:p>
                        <w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr>
                        <w:r><w:t>Item two.</w:t></w:r>
                    </w:p>
                    <w:sectPr>
                        <w:pgSz w:w="12240" w:h="15840"/>
                        <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>
                    </w:sectPr>
                </w:body>
            </w:document>
            """;

        const string numberingXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:abstractNum w:abstractNumId="0">
                    <w:lvl w:ilvl="0">
                        <w:start w:val="1"/>
                        <w:numFmt w:val="decimal"/>
                        <w:lvlText w:val="%1."/>
                        <w:lvlJc w:val="left"/>
                        <w:pPr><w:ind w:left="720"/></w:pPr>
                    </w:lvl>
                </w:abstractNum>
                <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
            </w:numbering>
            """;

        string documentRels = RelsXml(("rIdNumbering", NumberingRelType, "numbering.xml", null));

        return Package(
            ContentTypes(MainDocumentContentType, ("/word/numbering.xml", NumberingContentType)),
            documentXml,
            documentRels,
            extraXmlParts: new[] { ("word/numbering.xml", numberingXml) });
    }

    public static byte[] CustomStyles()
    {
        const string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <w:body>
                    <w:p>
                        <w:pPr><w:pStyle w:val="GoldenHeading"/></w:pPr>
                        <w:r><w:rPr><w:rStyle w:val="GoldenEmphasis"/></w:rPr><w:t>Custom styled GOLDEN heading</w:t></w:r>
                    </w:p>
                    <w:sectPr>
                        <w:pgSz w:w="12240" w:h="15840"/>
                        <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>
                    </w:sectPr>
                </w:body>
            </w:document>
            """;

        const string stylesXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:style w:type="paragraph" w:styleId="GoldenHeading">
                    <w:name w:val="Golden Heading"/>
                    <w:rPr><w:b/><w:color w:val="1F4E79"/></w:rPr>
                </w:style>
                <w:style w:type="character" w:styleId="GoldenEmphasis">
                    <w:name w:val="Golden Emphasis"/>
                    <w:rPr><w:i/></w:rPr>
                </w:style>
            </w:styles>
            """;

        string documentRels = RelsXml(("rIdStyles", StylesRelType, "styles.xml", null));

        return Package(
            ContentTypes(MainDocumentContentType, ("/word/styles.xml", StylesContentType)),
            documentXml,
            documentRels,
            extraXmlParts: new[] { ("word/styles.xml", stylesXml) });
    }

    public static byte[] Fields()
    {
        const string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <w:body>
                    <w:p>
                        <w:r><w:t xml:space="preserve">Page </w:t></w:r>
                        <w:r><w:fldChar w:fldCharType="begin"/></w:r>
                        <w:r><w:instrText xml:space="preserve"> PAGE \* MERGEFORMAT </w:instrText></w:r>
                        <w:r><w:fldChar w:fldCharType="separate"/></w:r>
                        <w:r><w:t>7</w:t></w:r>
                        <w:r><w:fldChar w:fldCharType="end"/></w:r>
                        <w:r><w:t xml:space="preserve"> of 9 GOLDEN field.</w:t></w:r>
                    </w:p>
                    <w:sectPr>
                        <w:pgSz w:w="12240" w:h="15840"/>
                        <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>
                    </w:sectPr>
                </w:body>
            </w:document>
            """;

        return Package(ContentTypes(MainDocumentContentType), documentXml);
    }

    public static byte[] TrackedChanges()
    {
        const string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <w:body>
                    <w:p>
                        <w:r><w:t xml:space="preserve">Final GOLDEN version. </w:t></w:r>
                        <w:ins w:id="101" w:author="Golden Author" w:date="2024-01-02T03:04:05Z">
                            <w:r><w:t xml:space="preserve">Inserted sentence.</w:t></w:r>
                        </w:ins>
                        <w:del w:id="102" w:author="Golden Author" w:date="2024-01-02T03:04:06Z">
                            <w:r><w:delText xml:space="preserve">Deleted sentence.</w:delText></w:r>
                        </w:del>
                    </w:p>
                    <w:sectPr>
                        <w:pgSz w:w="12240" w:h="15840"/>
                        <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>
                    </w:sectPr>
                </w:body>
            </w:document>
            """;

        return Package(ContentTypes(MainDocumentContentType), documentXml);
    }

    // ------------------------------------------------------------------
    // Hostile / resource fixtures
    // ------------------------------------------------------------------

    public static byte[] EntryExplosionPackage(int entryCount)
    {
        var parts = new List<(string Name, byte[] Content)>();
        for (int i = 0; i < entryCount; i++)
        {
            parts.Add(($"entry_{i}.txt", new byte[] { (byte)'a' }));
        }
        return Write(parts.ToArray());
    }

    public static byte[] OversizedSinglePartPackage(int partSizeBytes)
    {
        var largePart = new byte[partSizeBytes];
        return Package(ContentTypes(MainDocumentContentType), largePart);
    }

    public static byte[] OversizedTotalPackage(int partCount, int bytesPerPart)
    {
        var parts = new List<(string Name, byte[] Content)>
        {
            ("[Content_Types].xml", Utf8(ContentTypes(MainDocumentContentType)))
        };
        for (int i = 0; i < partCount; i++)
        {
            parts.Add(($"word/filler_{i}.xml", new byte[bytesPerPart]));
        }
        return Write(parts.ToArray());
    }

    public static byte[] HighlyCompressedPackage(int repeatCount)
    {
        string filler = new string('a', repeatCount);
        string documentXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:body><w:p><w:r><w:t>" + filler + "</w:t></w:r></w:p>" +
            "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr></w:body></w:document>";

        return Package(ContentTypes(MainDocumentContentType), documentXml);
    }

    public static byte[] ParserBudgetPackage(int occurrenceCount)
    {
        string body = string.Concat(Enumerable.Repeat("GOLDEN ", occurrenceCount));
        string documentXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:body><w:p><w:r><w:t xml:space=\"preserve\">" + body + "</w:t></w:r></w:p>" +
            "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr></w:body></w:document>";

        return Package(ContentTypes(MainDocumentContentType), documentXml);
    }

    public static byte[] DtdExternalEntityPackage()
    {
        const string documentXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE foo [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p><w:r><w:t>&xxe;</w:t></w:r></w:p>
                </w:body>
            </w:document>
            """;

        return Package(ContentTypes(MainDocumentContentType), documentXml);
    }

    public static byte[] ZipSlipPackage()
    {
        var parts = new List<(string Name, byte[] Content)>
        {
            ("[Content_Types].xml", Utf8(ContentTypes(MainDocumentContentType))),
            ("_rels/.rels", Utf8(RootRelsXml)),
            ("word/document.xml", Utf8("<?xml version=\"1.0\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body/></w:document>")),
            ("../escape.txt", Utf8("zip slip payload"))
        };
        return Write(parts.ToArray());
    }

    public static byte[] NotAZipPackage()
    {
        return Utf8("This is not a DOCX package; no ZIP structures exist here.");
    }

    public static byte[] MalformedXmlPackage()
    {
        const string documentXml = "<?xml version=\"1.0\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p></w:document>";
        return Package(ContentTypes(MainDocumentContentType), documentXml);
    }

    // ------------------------------------------------------------------
    // Package assembly
    // ------------------------------------------------------------------

    private const string RootRelsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """;

    private static string ContentTypes(string mainDocumentContentType, params (string PartName, string ContentType)[] overrides)
    {
        return ContentTypes(mainDocumentContentType, includePng: false, overrides);
    }

    private static string ContentTypes(
        string mainDocumentContentType,
        bool includePng,
        params (string PartName, string ContentType)[] overrides)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        if (includePng)
        {
            sb.Append("<Default Extension=\"png\" ContentType=\"image/png\"/>");
        }
        sb.Append("<Override PartName=\"/word/document.xml\" ContentType=\"");
        sb.Append(mainDocumentContentType);
        sb.Append("\"/>");
        foreach (var (partName, contentType) in overrides)
        {
            sb.Append("<Override PartName=\"");
            sb.Append(partName);
            sb.Append("\" ContentType=\"");
            sb.Append(contentType);
            sb.Append("\"/>");
        }
        sb.Append("</Types>");
        return sb.ToString();
    }

    private static string RelsXml(params (string Id, string Type, string Target, string? TargetMode)[] relationships)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        foreach (var (id, type, target, targetMode) in relationships)
        {
            sb.Append("<Relationship Id=\"");
            sb.Append(id);
            sb.Append("\" Type=\"");
            sb.Append(type);
            sb.Append("\" Target=\"");
            sb.Append(target);
            sb.Append('"');
            if (targetMode != null)
            {
                sb.Append(" TargetMode=\"");
                sb.Append(targetMode);
                sb.Append('"');
            }
            sb.Append("/>");
        }
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private static byte[] Package(
        string contentTypesXml,
        string documentXml,
        string? documentRelsXml = null,
        IEnumerable<(string Name, string Xml)>? extraXmlParts = null,
        IEnumerable<(string Name, byte[] Bytes)>? extraBinaryParts = null)
    {
        return Package(contentTypesXml, Utf8(documentXml), documentRelsXml, extraXmlParts, extraBinaryParts);
    }

    private static byte[] Package(
        string contentTypesXml,
        byte[] documentBytes,
        string? documentRelsXml = null,
        IEnumerable<(string Name, string Xml)>? extraXmlParts = null,
        IEnumerable<(string Name, byte[] Bytes)>? extraBinaryParts = null)
    {
        var parts = new List<(string Name, byte[] Content)>
        {
            ("[Content_Types].xml", Utf8(contentTypesXml)),
            ("_rels/.rels", Utf8(RootRelsXml)),
            ("word/document.xml", documentBytes)
        };

        if (documentRelsXml != null)
        {
            parts.Add(("word/_rels/document.xml.rels", Utf8(documentRelsXml)));
        }

        if (extraXmlParts != null)
        {
            foreach (var (name, xml) in extraXmlParts)
            {
                parts.Add((name, Utf8(xml)));
            }
        }

        if (extraBinaryParts != null)
        {
            foreach (var (name, bytes) in extraBinaryParts)
            {
                parts.Add((name, bytes));
            }
        }

        return Write(parts.ToArray());
    }

    private static byte[] Write(params (string Name, byte[] Content)[] parts)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in parts)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                // In Create mode the timestamp can only be set before the entry is opened for writing.
                entry.LastWriteTime = DeterministicTimestamp;
                using (var stream = entry.Open())
                {
                    stream.Write(content, 0, content.Length);
                }
            }
        }
        return memoryStream.ToArray();
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
}
