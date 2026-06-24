package com.vkenterprises.crmrs.utils

import android.content.Context
import android.content.Intent
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Typeface
import android.graphics.pdf.PdfDocument
import android.net.Uri
import android.text.Layout
import android.text.StaticLayout
import android.text.TextPaint
import androidx.core.content.FileProvider
import java.io.File
import java.io.FileOutputStream

/** Builds and shares the universal Pre-Repossession intimation PDF. */
object RepoPdf {

    data class LetterData(
        val dateText: String,
        val policeStation: String,
        val policeAddress: String,
        val loanAcNo: String,
        val vehicleRegNo: String,
        val assets: String,
        val borrowerName: String,
        val residenceAddress: String,
        val agencyName: String,
        val headOffice: String,
        val authLetterDate: String
    )

    // A4 @ 72dpi
    private const val PAGE_W = 595
    private const val PAGE_H = 842
    private const val MARGIN = 50f

    fun generate(context: Context, d: LetterData): File {
        val doc = PdfDocument()
        val pageInfo = PdfDocument.PageInfo.Builder(PAGE_W, PAGE_H, 1).create()
        val page = doc.startPage(pageInfo)
        val canvas = page.canvas

        val contentW = (PAGE_W - 2 * MARGIN).toInt()
        var y = MARGIN

        val normal = TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xFF000000.toInt(); textSize = 10.5f; typeface = Typeface.SANS_SERIF
        }
        val bold = TextPaint(normal).apply { typeface = Typeface.create(Typeface.SANS_SERIF, Typeface.BOLD) }
        val titlePaint = TextPaint(bold).apply { textSize = 12f }

        // Date (top-left)
        canvas.drawText("Date:  ${d.dateText}", MARGIN, y + 10f, bold)
        y += 28f

        // Centered, underlined title
        val title = "Pre-Repossession Intimation to Police Station"
        val titleW = titlePaint.measureText(title)
        val titleX = (PAGE_W - titleW) / 2f
        canvas.drawText(title, titleX, y + 10f, titlePaint)
        canvas.drawLine(titleX, y + 13f, titleX + titleW, y + 13f, titlePaint)
        y += 34f

        // To-block
        y = line(canvas, "To,", MARGIN, y, bold)
        y = line(canvas, "The Senior Inspector", MARGIN, y, bold)
        if (d.policeStation.isNotBlank()) y = line(canvas, d.policeStation, MARGIN, y, bold)
        if (d.policeAddress.isNotBlank())
            y = paragraph(canvas, d.policeAddress, MARGIN, y, contentW, bold)
        y += 10f

        y = line(canvas, "Dear Sir,", MARGIN, y, bold)
        y += 8f
        y = line(canvas, "Subject: Pre-Repo Intimation", MARGIN, y, bold)
        y += 10f

        // Field block: bold label, colon, value
        val labelW = 200f
        y = field(canvas, "Loan A/c No.", d.loanAcNo, MARGIN, y, labelW, contentW, bold, normal)
        y = field(canvas, "Vehicle Registration No.", d.vehicleRegNo, MARGIN, y, labelW, contentW, bold, normal)
        y = field(canvas, "Assets Details", d.assets, MARGIN, y, labelW, contentW, bold, normal)
        y = field(canvas, "Name of Borrower", d.borrowerName, MARGIN, y, labelW, contentW, bold, normal)
        y = field(canvas, "Residence Address of the Borrower", d.residenceAddress,
            MARGIN, y, labelW, contentW, bold, normal)
        y += 12f

        val agency = d.agencyName.ifBlank { "(Agency Name)" }
        val head   = d.headOffice.ifBlank { "the Bank" }
        val p1 = "We $agency (Agency Name) have been authorized by $head (“Bank”) vide " +
            "authorization letter dated ${d.authLetterDate}, for the purpose of recovering possession of " +
            "the above-mentioned vehicle. The above-named Borrower of the Bank has defaulted in abiding " +
            "by the terms and conditions of the Loan Agreement executed between the Borrower and the Bank."
        y = paragraph(canvas, p1, MARGIN, y, contentW, normal); y += 10f

        val p2 = "Pursuant to the rights of the Bank under the said Loan Agreement we, for and on behalf of " +
            "the Bank, are taking steps to recover possession of the aforesaid vehicle hypothecated by the " +
            "Borrower in favor of the Bank as a security for the loan availed under the said Loan Agreement. " +
            "It is expressly agreed by the said Borrower under the said Loan Agreement that in the event of " +
            "default under the said Loan Agreement, the Bank shall be entitled to exercise its rights available " +
            "under the Loan Agreement including but not limited to taking charge / possession of the aforesaid vehicle."
        y = paragraph(canvas, p2, MARGIN, y, contentW, normal); y += 10f

        val p3 = "This communication is for the purpose of your records. This shall also hold goods against any " +
            "prejudice or complaint filed by the Borrower or any of its representatives regarding the theft of " +
            "the aforesaid vehicle or any criminal complaint related thereto."
        y = paragraph(canvas, p3, MARGIN, y, contentW, normal); y += 16f

        y = line(canvas, "Thanking You,", MARGIN, y, normal); y += 16f
        line(canvas, head, MARGIN, y, bold)

        doc.finishPage(page)

        val safeRc = d.vehicleRegNo.replace(Regex("[^A-Za-z0-9]"), "").ifBlank { "vehicle" }
        val file = File(context.cacheDir, "PreRepo_${safeRc}_${System.currentTimeMillis()}.pdf")
        FileOutputStream(file).use { doc.writeTo(it) }
        doc.close()
        return file
    }

    fun share(context: Context, file: File) {
        val uri: Uri = FileProvider.getUriForFile(
            context, "${context.packageName}.fileprovider", file)
        val intent = Intent(Intent.ACTION_SEND).apply {
            type = "application/pdf"
            putExtra(Intent.EXTRA_STREAM, uri)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        context.startActivity(Intent.createChooser(intent, "Share Pre-Repossession letter"))
    }

    fun open(context: Context, file: File) {
        val uri: Uri = FileProvider.getUriForFile(
            context, "${context.packageName}.fileprovider", file)
        val intent = Intent(Intent.ACTION_VIEW).apply {
            setDataAndType(uri, "application/pdf")
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        runCatching { context.startActivity(intent) }
            .onFailure { share(context, file) }
    }

    // ---- drawing helpers ----

    private fun line(canvas: Canvas, text: String, x: Float, y: Float, paint: TextPaint): Float {
        canvas.drawText(text, x, y + paint.textSize, paint)
        return y + paint.textSize + 6f
    }

    private fun paragraph(canvas: Canvas, text: String, x: Float, y: Float,
                          width: Int, paint: TextPaint): Float {
        val layout = StaticLayout.Builder
            .obtain(text, 0, text.length, paint, width)
            .setAlignment(Layout.Alignment.ALIGN_NORMAL)
            .setLineSpacing(2f, 1f)
            .setIncludePad(false)
            .build()
        canvas.save()
        canvas.translate(x, y)
        layout.draw(canvas)
        canvas.restore()
        return y + layout.height + 2f
    }

    private fun field(canvas: Canvas, label: String, value: String, x: Float, y: Float,
                      labelW: Float, contentW: Int, bold: TextPaint, normal: TextPaint): Float {
        canvas.drawText(label, x, y + bold.textSize, bold)
        canvas.drawText(":", x + labelW, y + bold.textSize, bold)
        val valX = x + labelW + 12f
        val valW = (contentW - labelW - 12f).toInt().coerceAtLeast(80)
        val v = value.ifBlank { "" }
        val layout = StaticLayout.Builder
            .obtain(v, 0, v.length, normal, valW)
            .setAlignment(Layout.Alignment.ALIGN_NORMAL)
            .setIncludePad(false)
            .build()
        canvas.save()
        canvas.translate(valX, y)
        layout.draw(canvas)
        canvas.restore()
        val used = maxOf(bold.textSize + 6f, layout.height.toFloat() + 2f)
        return y + used + 6f
    }
}
