package com.vkenterprises.crmrs.utils

import android.content.Context
import android.graphics.Bitmap
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.FileOutputStream
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

object RepoDocx {

    fun generate(context: Context, d: RepoPdf.LetterData): File {
        val logoBytes: ByteArray? = d.logo?.let { bmp ->
            ByteArrayOutputStream().use { bmp.compress(Bitmap.CompressFormat.JPEG, 85, it); it.toByteArray() }
        }
        val (logoCx, logoCy) = logoEmu(d.logo)

        val docXml = buildDocumentXml(d, logoBytes != null, logoCx, logoCy)

        val file = File(context.cacheDir, RepoPdf.fileName(d, "docx"))
        ZipOutputStream(FileOutputStream(file)).use { zip ->
            put(zip, "[Content_Types].xml", contentTypes())
            put(zip, "_rels/.rels", rootRels())
            put(zip, "word/_rels/document.xml.rels", documentRels(logoBytes != null))
            put(zip, "word/document.xml", docXml)
            if (logoBytes != null) put(zip, "word/media/image1.jpeg", logoBytes)
        }
        return file
    }

    private fun logoEmu(bmp: Bitmap?): Pair<Long, Long> {
        if (bmp == null || bmp.width <= 0 || bmp.height <= 0) return 0L to 0L
        val cx = 1371600L
        val cy = (cx.toDouble() * bmp.height / bmp.width).toLong()
        return cx to cy
    }

    private fun buildDocumentXml(d: RepoPdf.LetterData, hasLogo: Boolean, cx: Long, cy: Long): String {
        val head = d.headOffice.ifBlank { "the Bank" }
        val agency = d.agencyName.ifBlank { "(Agency Name)" }
        val sb = StringBuilder()
        sb.append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
        sb.append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"")
        sb.append(" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"")
        sb.append(" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\"")
        sb.append(" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"")
        sb.append(" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><w:body>")

        if (hasLogo) sb.append(logoParagraph(cx, cy))

        sb.append(para(run("Date:  ${d.dateText}", bold = true)))
        sb.append(para(run(d.docType.title, bold = true, underline = true), align = "center"))
        sb.append(para(run("To,", bold = true)))
        sb.append(para(run("The Senior Inspector", bold = true)))
        if (d.policeStation.isNotBlank()) sb.append(para(run(d.policeStation, bold = true)))
        if (d.policeAddress.isNotBlank()) sb.append(para(run(d.policeAddress, bold = true)))
        sb.append(para(run("Dear Sir,", bold = true)))
        sb.append(para(run("Subject: ${d.docType.subject}", bold = true)))

        sb.append(fieldPara("Loan A/c No.", d.loanAcNo))
        sb.append(fieldPara("Vehicle Registration No.", d.vehicleRegNo))
        sb.append(fieldPara("Assets Details", d.assets))
        sb.append(fieldPara("Name of Borrower", d.borrowerName))
        sb.append(fieldPara("Residence Address of the Borrower", d.residenceAddress))

        val p1 = "We $agency (Agency Name) have been authorized by $head (“Bank”) vide " +
            "authorization letter dated ${d.authLetterDate}, for the purpose of recovering possession of " +
            "the above-mentioned vehicle. The above-named Borrower of the Bank has defaulted in abiding " +
            "by the terms and conditions of the Loan Agreement executed between the Borrower and the Bank."
        sb.append(para(run(p1)))
        sb.append(para(run(RepoPdf.body2(d.docType, head))))
        val p3 = "This communication is for the purpose of your records. This shall also hold goods against any " +
            "prejudice or complaint filed by the Borrower or any of its representatives regarding the theft of " +
            "the aforesaid vehicle or any criminal complaint related thereto."
        sb.append(para(run(p3)))

        sb.append(para(run("Thanking You,")))
        sb.append(para(run(head, bold = true)))

        sb.append("<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>")
        sb.append("<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\"/></w:sectPr>")
        sb.append("</w:body></w:document>")
        return sb.toString()
    }

    private fun logoParagraph(cx: Long, cy: Long): String =
        "<w:p><w:pPr><w:jc w:val=\"right\"/></w:pPr><w:r><w:drawing>" +
        "<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">" +
        "<wp:extent cx=\"$cx\" cy=\"$cy\"/><wp:docPr id=\"1\" name=\"logo\"/>" +
        "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic>" +
        "<pic:nvPicPr><pic:cNvPr id=\"1\" name=\"logo\"/><pic:cNvPicPr/></pic:nvPicPr>" +
        "<pic:blipFill><a:blip r:embed=\"rId1\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
        "<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"$cx\" cy=\"$cy\"/></a:xfrm>" +
        "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>" +
        "</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>"

    private fun fieldPara(label: String, value: String): String =
        para(run(label, bold = true) + run("  :  ", bold = true) + run(value))

    private fun para(runsXml: String, align: String? = null): String {
        val pPr = if (align != null) "<w:pPr><w:jc w:val=\"$align\"/></w:pPr>" else ""
        return "<w:p>$pPr$runsXml</w:p>"
    }

    private fun run(text: String, bold: Boolean = false, underline: Boolean = false): String {
        val rPr = StringBuilder("<w:rPr>")
        if (bold) rPr.append("<w:b/>")
        if (underline) rPr.append("<w:u w:val=\"single\"/>")
        rPr.append("<w:sz w:val=\"21\"/><w:szCs w:val=\"21\"/></w:rPr>")
        return "<w:r>$rPr<w:t xml:space=\"preserve\">${esc(text)}</w:t></w:r>"
    }

    private fun esc(s: String): String =
        s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
            .replace("\"", "&quot;").replace("'", "&apos;")

    private fun contentTypes(): String =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/>" +
        "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
        "</Types>"

    private fun rootRels(): String =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
        "</Relationships>"

    private fun documentRels(hasLogo: Boolean): String {
        val img = if (hasLogo)
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image1.jpeg\"/>"
        else ""
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">$img</Relationships>"
    }

    private fun put(zip: ZipOutputStream, name: String, content: String) {
        put(zip, name, content.toByteArray(Charsets.UTF_8))
    }

    private fun put(zip: ZipOutputStream, name: String, bytes: ByteArray) {
        zip.putNextEntry(ZipEntry(name))
        zip.write(bytes)
        zip.closeEntry()
    }
}
