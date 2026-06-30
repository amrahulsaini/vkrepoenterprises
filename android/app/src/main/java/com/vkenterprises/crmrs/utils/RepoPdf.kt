package com.vkenterprises.crmrs.utils

import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Typeface
import android.graphics.pdf.PdfDocument
import android.net.Uri
import android.text.Layout
import android.text.StaticLayout
import android.text.TextPaint
import androidx.core.content.FileProvider
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.io.FileOutputStream

enum class RepoDocType(val title: String, val subject: String) {
    PRE("Pre-Repossession Intimation to Police Station", "Pre-Repo Intimation"),
    POST("Post-Repossession Intimation to Police Station", "Post-Repo Intimation")
}

object RepoPdf {

    data class LetterData(
        val docType: RepoDocType,
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
        val authLetterDate: String,
        val logo: Bitmap? = null
    )

    private const val PAGE_W = 595
    private const val PAGE_H = 842
    private const val MARGIN = 50f

    suspend fun loadBitmap(url: String): Bitmap? = withContext(Dispatchers.IO) {
        runCatching {
            val bytes = com.vkenterprises.crmrs.data.api.ApiClient.downloadBytes(url)
            if (bytes != null) BitmapFactory.decodeByteArray(bytes, 0, bytes.size) else null
        }.getOrNull()
    }

    fun body2(type: RepoDocType, headWord: String): String = when (type) {
        RepoDocType.PRE ->
            "Pursuant to the rights of the Bank under the said Loan Agreement we, for and on behalf of " +
            "the Bank, are taking steps to recover possession of the aforesaid vehicle hypothecated by the " +
            "Borrower in favor of the Bank as a security for the loan availed under the said Loan Agreement. " +
            "It is expressly agreed by the said Borrower under the said Loan Agreement that in the event of " +
            "default under the said Loan Agreement, the Bank shall be entitled to exercise its rights available " +
            "under the Loan Agreement including but not limited to taking charge / possession of the aforesaid vehicle."
        RepoDocType.POST ->
            "Pursuant to the rights of the Bank under the said Loan Agreement we, for and on behalf of " +
            "the Bank, have taken possession of the aforesaid vehicle hypothecated by the Borrower in favor " +
            "of the Bank as a security for the loan availed under the said Loan Agreement. It is expressly " +
            "agreed by the said Borrower under the said Loan Agreement that in the event of default under the " +
            "said Loan Agreement, the Bank shall be entitled to exercise its rights available under the Loan " +
            "Agreement including but not limited to taking charge / possession of the aforesaid vehicle."
    }

    fun generate(context: Context, d: LetterData): File {
        val doc = PdfDocument()
        val pageInfo = PdfDocument.PageInfo.Builder(PAGE_W, PAGE_H, 1).create()
        val page = doc.startPage(pageInfo)
        val canvas = page.canvas

        val contentW = (PAGE_W - 2 * MARGIN).toInt()

        val normal = TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xFF000000.toInt(); textSize = 10.5f; typeface = Typeface.SANS_SERIF
        }
        val bold = TextPaint(normal).apply { typeface = Typeface.create(Typeface.SANS_SERIF, Typeface.BOLD) }
        val titlePaint = TextPaint(bold).apply { textSize = 12f }

        var y = MARGIN

        d.logo?.let { bmp ->
            if (bmp.width > 0 && bmp.height > 0) {
                val maxW = 120f
                val maxH = 70f
                val ratio = minOf(maxW / bmp.width, maxH / bmp.height)
                val w = bmp.width * ratio
                val h = bmp.height * ratio
                val left = PAGE_W - MARGIN - w
                canvas.drawBitmap(bmp, null, RectF(left, MARGIN, left + w, MARGIN + h), null)
                y = maxOf(y, MARGIN + h + 10f)
            }
        }

        canvas.drawText("Date:  ${d.dateText}", MARGIN, y + 10f, bold)
        y += 28f

        val title = d.docType.title
        val titleW = titlePaint.measureText(title)
        val titleX = (PAGE_W - titleW) / 2f
        canvas.drawText(title, titleX, y + 10f, titlePaint)
        canvas.drawLine(titleX, y + 13f, titleX + titleW, y + 13f, titlePaint)
        y += 34f

        y = line(canvas, "To,", MARGIN, y, bold)
        y = line(canvas, "The Senior Inspector", MARGIN, y, bold)
        if (d.policeStation.isNotBlank()) y = line(canvas, d.policeStation, MARGIN, y, bold)
        if (d.policeAddress.isNotBlank()) y = paragraph(canvas, d.policeAddress, MARGIN, y, contentW, bold)
        y += 10f

        y = line(canvas, "Dear Sir,", MARGIN, y, bold)
        y += 8f
        y = line(canvas, "Subject: ${d.docType.subject}", MARGIN, y, bold)
        y += 10f

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

        y = paragraph(canvas, body2(d.docType, head), MARGIN, y, contentW, normal); y += 10f

        val p3 = "This communication is for the purpose of your records. This shall also hold goods against any " +
            "prejudice or complaint filed by the Borrower or any of its representatives regarding the theft of " +
            "the aforesaid vehicle or any criminal complaint related thereto."
        y = paragraph(canvas, p3, MARGIN, y, contentW, normal); y += 16f

        y = line(canvas, "Thanking You,", MARGIN, y, normal); y += 16f
        line(canvas, head, MARGIN, y, bold)

        doc.finishPage(page)

        val file = File(context.cacheDir, fileName(d, "pdf"))
        FileOutputStream(file).use { doc.writeTo(it) }
        doc.close()
        return file
    }

    fun fileName(d: LetterData, ext: String): String {
        val prefix = if (d.docType == RepoDocType.PRE) "PreRepo" else "PostRepo"
        val safeRc = d.vehicleRegNo.replace(Regex("[^A-Za-z0-9]"), "").ifBlank { "vehicle" }
        return "${prefix}_${safeRc}_${System.currentTimeMillis()}.$ext"
    }

    fun share(context: Context, file: File, mime: String) {
        val uri: Uri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
        val intent = Intent(Intent.ACTION_SEND).apply {
            type = mime
            putExtra(Intent.EXTRA_STREAM, uri)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        context.startActivity(Intent.createChooser(intent, "Share letter"))
    }

    fun open(context: Context, file: File, mime: String) {
        val uri: Uri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
        val intent = Intent(Intent.ACTION_VIEW).apply {
            setDataAndType(uri, mime)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        runCatching { context.startActivity(intent) }.onFailure { share(context, file, mime) }
    }

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
