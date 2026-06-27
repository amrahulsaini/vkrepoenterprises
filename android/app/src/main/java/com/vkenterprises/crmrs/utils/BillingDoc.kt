package com.vkenterprises.crmrs.utils

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Typeface
import android.graphics.drawable.BitmapDrawable
import android.graphics.pdf.PdfDocument
import android.text.Layout
import android.text.StaticLayout
import android.text.TextPaint
import androidx.core.content.ContextCompat
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.FileOutputStream
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

object BillingDoc {

    data class BillingData(
        val logo: Bitmap?,
        val agencyName: String,
        val headerAddress: String,
        val headerContact: String,
        val headerEmail: String,
        val toFinance: String,
        val invoiceDate: String,
        val invoiceNo: String,
        val branch: String,
        val confirmationBy: String,
        val agriLoanNo: String,
        val customerName: String,
        val makeModel: String,
        val rcNo: String,
        val dateOfRepossession: String,
        val parkingYard: String,
        val agencyNameRow: String,
        val enclosed: String,
        val qty: String,
        val repoChargesWords: String,
        val repoChargesAmount: String,
        val additionalCharges: String,
        val panNo: String,
        val gstState: String,
        val bankAccountName: String,
        val accountNo: String,
        val ifscCode: String,
        val bankBranch: String,
        val totalGrossWords: String,
        val totalGrossAmount: String,
        val paymentName: String,
        val footerLine: String
    )

    private data class BRow(
        val label: String,
        val value: String = "",
        val amount: String = "",
        val bold: Boolean = false,
        val span: Boolean = false,
        val header: Boolean = false
    )

    private fun rows(d: BillingData): List<BRow> = listOf(
        BRow("INVOICE DATE", d.invoiceDate),
        BRow("INVOICE NO", d.invoiceNo),
        BRow("BRANCH", d.branch),
        BRow("CONFIRMATION BY", d.confirmationBy),
        BRow("DESCRIPTION EXPENSE", "ALL DETAILS", "AMOUNT", bold = true, header = true),
        BRow("AGRI-LOAN NO", d.agriLoanNo),
        BRow("NAME OF CUSTOMER", d.customerName),
        BRow("MAKE-MODEL", d.makeModel),
        BRow("RC NO", d.rcNo),
        BRow("DATE OF REPOSSESSION", d.dateOfRepossession),
        BRow("PARKING YARD NAME", d.parkingYard),
        BRow("NAME OF AGNCY", d.agencyNameRow),
        BRow("ENCLOSED", d.enclosed),
        BRow("QTY", d.qty),
        BRow("REPO CHARGES", d.repoChargesWords, d.repoChargesAmount),
        BRow("ADDITIONAL CHARGES", d.additionalCharges),
        BRow("PAN NO", d.panNo),
        BRow("GST STATE", d.gstState),
        BRow("BANK ACCOUNT NAME", d.bankAccountName),
        BRow("ACCOUNT NO", d.accountNo),
        BRow("IFSC CODE", d.ifscCode),
        BRow("BRANCH", d.bankBranch),
        BRow("TOTAL GROSS AMOUNT", d.totalGrossWords, d.totalGrossAmount, bold = true),
        BRow("KINDLY RELEASE THE PAYMENT IN THE NAME OF ${d.paymentName}", span = true)
    )

    fun drawableToBitmap(context: Context, resId: Int): Bitmap? {
        val dr = ContextCompat.getDrawable(context, resId) ?: return null
        if (dr is BitmapDrawable && dr.bitmap != null) return dr.bitmap
        val w = dr.intrinsicWidth.coerceAtLeast(1)
        val h = dr.intrinsicHeight.coerceAtLeast(1)
        val bmp = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888)
        val c = Canvas(bmp)
        dr.setBounds(0, 0, w, h)
        dr.draw(c)
        return bmp
    }

    private const val PAGE_W = 595
    private const val PAGE_H = 842
    private const val MARGIN = 36f

    fun generatePdf(context: Context, d: BillingData): File {
        val doc = PdfDocument()
        val pageInfo = PdfDocument.PageInfo.Builder(PAGE_W, PAGE_H, 1).create()
        val page = doc.startPage(pageInfo)
        val canvas = page.canvas

        val x0 = MARGIN
        val contentW = PAGE_W - 2 * MARGIN

        val small = TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xFF000000.toInt(); textSize = 8.5f; typeface = Typeface.SANS_SERIF
        }
        val smallBold = TextPaint(small).apply { typeface = Typeface.create(Typeface.SANS_SERIF, Typeface.BOLD) }
        val name = TextPaint(smallBold).apply { textSize = 20f }
        val center = TextPaint(small).apply { textAlign = Paint.Align.CENTER }
        val centerBold = TextPaint(smallBold).apply { textAlign = Paint.Align.CENTER }

        var y = MARGIN
        d.logo?.let { bmp ->
            if (bmp.width > 0 && bmp.height > 0) {
                val h = 54f
                val w = bmp.width * (h / bmp.height)
                canvas.drawBitmap(bmp, null, RectF(x0, y, x0 + w, y + h), null)
            }
        }
        canvas.drawText(d.agencyName, PAGE_W / 2f, y + 24f, name.apply { textAlign = Paint.Align.CENTER })
        var hy = y + 38f
        if (d.headerAddress.isNotBlank()) { canvas.drawText(d.headerAddress, PAGE_W / 2f, hy, center); hy += 12f }
        if (d.headerContact.isNotBlank()) { canvas.drawText(d.headerContact, PAGE_W / 2f, hy, center); hy += 12f }
        if (d.headerEmail.isNotBlank())   { canvas.drawText(d.headerEmail, PAGE_W / 2f, hy, center); hy += 12f }
        y = hy + 4f
        val rule = Paint().apply { color = 0xFFB00020.toInt(); strokeWidth = 1.4f }
        canvas.drawLine(x0, y, x0 + contentW, y, rule)
        y += 16f

        canvas.drawText("To,  ${d.toFinance},", PAGE_W / 2f, y, centerBold); y += 13f
        canvas.drawText("SUBJECT–SUBMISSION OF REPOSSESSION BILL.", PAGE_W / 2f, y, centerBold); y += 16f

        val border = Paint().apply { color = 0xFF000000.toInt(); strokeWidth = 0.8f; style = Paint.Style.STROKE }
        val labelW = contentW * 0.34f
        val detailW = contentW * 0.46f
        val amtW = contentW - labelW - detailW
        val pad = 4f

        for (row in rows(d)) {
            if (row.span) {
                val layout = textLayout(row.label, smallBold, (contentW - 2 * pad).toInt())
                val h = layout.height + 2 * pad
                canvas.drawRect(x0, y, x0 + contentW, y + h, border)
                drawLayout(canvas, layout, x0 + pad, y + pad)
                y += h
                continue
            }
            val valPaint = if (row.bold) smallBold else small
            val labelLayout = textLayout(row.label, smallBold, (labelW - 2 * pad).toInt())
            val valLayout = textLayout(row.value, valPaint, (detailW - 2 * pad).toInt())
            val h = maxOf(labelLayout.height, valLayout.height) + 2 * pad
            canvas.drawRect(x0, y, x0 + contentW, y + h, border)
            canvas.drawLine(x0 + labelW, y, x0 + labelW, y + h, border)
            canvas.drawLine(x0 + labelW + detailW, y, x0 + labelW + detailW, y + h, border)
            drawLayout(canvas, labelLayout, x0 + pad, y + pad)
            drawLayout(canvas, valLayout, x0 + labelW + pad, y + pad)
            if (row.amount.isNotBlank())
                canvas.drawText(row.amount, x0 + labelW + detailW + amtW - pad, y + smallBold.textSize + pad,
                    smallBold.apply { textAlign = Paint.Align.RIGHT })
            smallBold.textAlign = Paint.Align.LEFT
            y += h
        }

        y += 24f
        val right = TextPaint(smallBold).apply { textAlign = Paint.Align.RIGHT }
        canvas.drawText("Thank You", x0 + contentW, y, right); y += 13f
        canvas.drawText(d.agencyName, x0 + contentW, y, right); y += 13f
        if (d.footerLine.isNotBlank()) canvas.drawText(d.footerLine, x0 + contentW, y, right)

        doc.finishPage(page)
        val file = File(context.cacheDir, fileName(d, "pdf"))
        FileOutputStream(file).use { doc.writeTo(it) }
        doc.close()
        return file
    }

    private fun textLayout(text: String, paint: TextPaint, width: Int): StaticLayout =
        StaticLayout.Builder.obtain(text, 0, text.length, paint, width.coerceAtLeast(20))
            .setAlignment(Layout.Alignment.ALIGN_NORMAL).setIncludePad(false).build()

    private fun drawLayout(canvas: Canvas, layout: StaticLayout, x: Float, y: Float) {
        canvas.save(); canvas.translate(x, y); layout.draw(canvas); canvas.restore()
    }

    private fun fileName(d: BillingData, ext: String): String {
        val safe = d.rcNo.replace(Regex("[^A-Za-z0-9]"), "").ifBlank { "bill" }
        return "RepoBill_${safe}_${System.currentTimeMillis()}.$ext"
    }

    fun generateDocx(context: Context, d: BillingData): File {
        val logoBytes: ByteArray? = d.logo?.let { bmp ->
            ByteArrayOutputStream().use { bmp.compress(Bitmap.CompressFormat.JPEG, 85, it); it.toByteArray() }
        }
        val cx = 1143000L
        val cy = if (d.logo != null && d.logo.width > 0)
            (cx.toDouble() * d.logo.height / d.logo.width).toLong() else 0L

        val sb = StringBuilder()
        sb.append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
        sb.append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"")
        sb.append(" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"")
        sb.append(" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\"")
        sb.append(" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"")
        sb.append(" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><w:body>")

        if (logoBytes != null) sb.append(logoPara(cx, cy))
        sb.append(p(run(d.agencyName, bold = true, sz = 36), "center"))
        if (d.headerAddress.isNotBlank()) sb.append(p(run(d.headerAddress), "center"))
        if (d.headerContact.isNotBlank()) sb.append(p(run(d.headerContact), "center"))
        if (d.headerEmail.isNotBlank())   sb.append(p(run(d.headerEmail), "center"))
        sb.append(p(run("To,  ${d.toFinance},", bold = true), "center"))
        sb.append(p(run("SUBJECT–SUBMISSION OF REPOSSESSION BILL.", bold = true), "center"))

        sb.append("<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/>")
        sb.append("<w:tblBorders><w:top w:val=\"single\" w:sz=\"6\"/><w:left w:val=\"single\" w:sz=\"6\"/>")
        sb.append("<w:bottom w:val=\"single\" w:sz=\"6\"/><w:right w:val=\"single\" w:sz=\"6\"/>")
        sb.append("<w:insideH w:val=\"single\" w:sz=\"6\"/><w:insideV w:val=\"single\" w:sz=\"6\"/></w:tblBorders></w:tblPr>")
        sb.append("<w:tblGrid><w:gridCol w:w=\"3200\"/><w:gridCol w:w=\"4300\"/><w:gridCol w:w=\"1900\"/></w:tblGrid>")
        for (row in rows(d)) {
            if (row.span) {
                sb.append("<w:tr><w:tc><w:tcPr><w:gridSpan w:val=\"3\"/></w:tcPr>")
                sb.append(p(run(row.label, bold = true))).append("</w:tc></w:tr>")
            } else {
                sb.append("<w:tr>")
                sb.append("<w:tc>").append(p(run(row.label, bold = true))).append("</w:tc>")
                sb.append("<w:tc>").append(p(run(row.value, bold = row.bold))).append("</w:tc>")
                sb.append("<w:tc>").append(p(run(row.amount, bold = true))).append("</w:tc>")
                sb.append("</w:tr>")
            }
        }
        sb.append("</w:tbl>")

        sb.append(p(run("", false)))
        sb.append(p(run("Thank You", bold = true), "right"))
        sb.append(p(run(d.agencyName, bold = true), "right"))
        if (d.footerLine.isNotBlank()) sb.append(p(run(d.footerLine, bold = true), "right"))

        sb.append("<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>")
        sb.append("<w:pgMar w:top=\"720\" w:right=\"720\" w:bottom=\"720\" w:left=\"720\"/></w:sectPr>")
        sb.append("</w:body></w:document>")

        val file = File(context.cacheDir, fileName(d, "docx"))
        ZipOutputStream(FileOutputStream(file)).use { zip ->
            put(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "</Types>")
            put(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                "</Relationships>")
            val imgRel = if (logoBytes != null)
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image1.jpeg\"/>" else ""
            put(zip, "word/_rels/document.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">$imgRel</Relationships>")
            put(zip, "word/document.xml", sb.toString())
            if (logoBytes != null) put(zip, "word/media/image1.jpeg", logoBytes)
        }
        return file
    }

    private fun logoPara(cx: Long, cy: Long): String =
        "<w:p><w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">" +
        "<wp:extent cx=\"$cx\" cy=\"$cy\"/><wp:docPr id=\"1\" name=\"logo\"/>" +
        "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic>" +
        "<pic:nvPicPr><pic:cNvPr id=\"1\" name=\"logo\"/><pic:cNvPicPr/></pic:nvPicPr>" +
        "<pic:blipFill><a:blip r:embed=\"rId1\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
        "<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"$cx\" cy=\"$cy\"/></a:xfrm>" +
        "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>" +
        "</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>"

    private fun p(runsXml: String, align: String? = null): String {
        val pPr = if (align != null) "<w:pPr><w:jc w:val=\"$align\"/></w:pPr>" else ""
        return "<w:p>$pPr$runsXml</w:p>"
    }

    private fun run(text: String, bold: Boolean = false, sz: Int = 18): String {
        val b = if (bold) "<w:b/>" else ""
        return "<w:r><w:rPr>$b<w:sz w:val=\"$sz\"/><w:szCs w:val=\"$sz\"/></w:rPr>" +
            "<w:t xml:space=\"preserve\">${esc(text)}</w:t></w:r>"
    }

    private fun esc(s: String): String =
        s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
            .replace("\"", "&quot;").replace("'", "&apos;")

    private fun put(zip: ZipOutputStream, name: String, content: String) =
        put(zip, name, content.toByteArray(Charsets.UTF_8))

    private fun put(zip: ZipOutputStream, name: String, bytes: ByteArray) {
        zip.putNextEntry(ZipEntry(name)); zip.write(bytes); zip.closeEntry()
    }
}
