package com.vkenterprises.crmrs.utils

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Typeface
import android.graphics.pdf.PdfDocument
import android.text.Layout
import android.text.StaticLayout
import android.text.TextPaint
import java.io.File
import java.io.FileOutputStream

object AuthorityLetterPdf {

    data class Data(
        val agencyName: String,
        val regNo: String,
        val gstNo: String,
        val dateText: String,
        val bankNbfc: String,
        val loanAcNo: String,
        val borrowerName: String,
        val vehicleNo: String,
        val chassisNo: String,
        val engineNo: String,
        val authorizedExecutive: String,
        val executiveId: String,
        val letterhead: Bitmap? = null
    )

    private const val PAGE_W = 595
    private const val PAGE_H = 842
    private const val MARGIN = 42f

    private val TERMS = listOf(
        "The authorized representative shall act only in accordance with applicable law, the concerned Bank/NBFC approved policy, contractual instructions, and applicable RBI directions/guidelines.",
        "The Borrower/Customer shall be treated with dignity and professionalism at all times. No threat, harassment, abusive language, undue pressure, intimidation, or physical force shall be used.",
        "Possession of the vehicle, wherever legally permissible, shall be taken only through a lawful and peaceful process. No forcible entry, unlawful restraint, or other illegal method shall be adopted.",
        "Where required by applicable law or the Bank/NBFC approved practice, appropriate intimation/acknowledgement shall be given to or obtained from the concerned Police Station.",
        "At the time of taking possession, a Vehicle Inventory / Possession Memo shall be prepared, recording the condition of the vehicle, accessories, documents and personal articles, if any, and acknowledgement shall be obtained wherever applicable.",
        "The representative shall not collect or accept any unauthorized cash or personal payment from the Customer. Any payment shall be accepted only through channels expressly approved by the concerned Bank/NBFC.",
        "All Customer, loan and vehicle information shall be kept confidential and used solely for the authorized purpose, subject to applicable privacy and data-protection requirements.",
        "The representative shall carry a valid Identity Card and this Letter of Authority during the assignment and shall produce the same when reasonably required.",
        "This authority is limited to the specific assignment and validity period stated herein and does not create any employment, agency, ownership, or other authority beyond what is expressly stated in this letter."
    )

    fun fileName(d: Data): String {
        val v = d.vehicleNo.ifBlank { "vehicle" }.replace(Regex("[^A-Za-z0-9]"), "")
        return "AuthorityLetter_" + v + ".pdf"
    }

    fun generate(context: Context, d: Data): File {
        val doc = PdfDocument()
        val page = doc.startPage(PdfDocument.PageInfo.Builder(PAGE_W, PAGE_H, 1).create())
        val canvas = page.canvas
        val contentW = (PAGE_W - 2 * MARGIN).toInt()

        val normal = TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xFF000000.toInt(); textSize = 8.2f; typeface = Typeface.SANS_SERIF
        }
        val bold = TextPaint(normal).apply {
            typeface = Typeface.create(Typeface.SANS_SERIF, Typeface.BOLD)
        }
        val small = TextPaint(normal).apply { textSize = 7f; color = 0xFF666666.toInt() }
        val title = TextPaint(bold).apply { textSize = 14f }
        val subTitle = TextPaint(normal).apply { textSize = 8.5f }
        val rule = Paint().apply { color = 0xFF000000.toInt(); strokeWidth = 0.7f }
        val boxPaint = Paint().apply {
            style = Paint.Style.STROKE; strokeWidth = 0.8f; color = 0xFF000000.toInt()
        }

        var y = MARGIN

        d.letterhead?.let { bmp ->
            if (bmp.width > 0 && bmp.height > 0) {
                val w = PAGE_W - 2 * MARGIN
                val h = (bmp.height * (w / bmp.width)).coerceAtMost(150f)
                canvas.drawBitmap(bmp, null, RectF(MARGIN, y, MARGIN + w, y + h), null)
                y += h + 8f
                canvas.drawLine(MARGIN, y, PAGE_W - MARGIN, y, rule)
                y += 14f
            }
        }

        canvas.drawText("REG NO: " + d.regNo, MARGIN, y + 8f, bold)
        canvas.drawText("Date: " + d.dateText, PAGE_W - MARGIN - 160f, y + 8f, bold)
        y += 14f
        canvas.drawText("GST NO: " + d.gstNo, MARGIN, y + 8f, bold)
        y += 26f

        val t = "LETTER OF AUTHORITY"
        canvas.drawText(t, (PAGE_W - title.measureText(t)) / 2f, y + 12f, title)
        y += 20f
        val st = "(Vehicle Repossession / Recovery Assistance)"
        canvas.drawText(st, (PAGE_W - subTitle.measureText(st)) / 2f, y + 8f, subTitle)
        y += 24f

        val boxTop = y
        val colW = contentW / 2f
        val leftRows = listOf(
            "Bank / NBFC" to d.bankNbfc,
            "Borrower Name" to d.borrowerName,
            "Chassis No" to d.chassisNo,
            "Authorized Executive" to d.authorizedExecutive
        )
        val rightRows = listOf(
            "Loan A/c No" to d.loanAcNo,
            "Vehicle No" to d.vehicleNo,
            "Engine No" to d.engineNo,
            "Executive ID" to d.executiveId
        )
        leftRows.forEachIndexed { i, pair ->
            val ry = boxTop + i * 20f
            drawPair(canvas, pair.first, pair.second, MARGIN + 8f, ry, colW - 16f, bold, normal, rule)
            drawPair(canvas, rightRows[i].first, rightRows[i].second,
                MARGIN + colW + 8f, ry, colW - 16f, bold, normal, rule)
        }
        y = boxTop + leftRows.size * 20f
        canvas.drawRect(MARGIN, boxTop - 6f, PAGE_W - MARGIN, y, boxPaint)
        y += 16f

        val intro = "The above-named representative is hereby authorized, strictly subject to the written instructions " +
            "of the concerned Bank/NBFC and applicable law, to assist in lawful recovery/repossession-related " +
            "activities in respect of the vehicle described above. This authority does not permit the use of force, " +
            "coercion, intimidation, trespass, or any act prohibited by law."
        y = para(canvas, intro, MARGIN, y, contentW, normal) + 12f

        canvas.drawText("TERMS, CONDITIONS & COMPLIANCE INSTRUCTIONS", MARGIN, y + 8f, bold)
        y += 16f

        TERMS.forEachIndexed { i, term ->
            canvas.drawText((i + 1).toString() + ".", MARGIN, y + 7f, bold)
            y = para(canvas, term, MARGIN + 16f, y, contentW - 16, normal) + 5f
        }

        y += 14f
        canvas.drawText("Validity:  From ______________   To ______________", MARGIN, y + 8f, bold)
        canvas.drawText("For " + d.agencyName, PAGE_W - MARGIN - 170f, y + 8f, bold)
        y += 48f
        canvas.drawText("Authorized Signatory", PAGE_W - MARGIN - 170f, y, bold)
        y += 15f
        canvas.drawText("Name: ____________________", PAGE_W - MARGIN - 170f, y, normal)
        y += 15f
        canvas.drawText("Seal:", PAGE_W - MARGIN - 170f, y, normal)
        y += 22f

        val note = "Note: This document is an internal authorization/compliance format. It should be used only after " +
            "approval by the concerned Bank/NBFC and, where appropriate, review by its legal/compliance team."
        para(canvas, note, MARGIN, y, contentW, small)

        doc.finishPage(page)
        val file = File(context.cacheDir, fileName(d))
        FileOutputStream(file).use { doc.writeTo(it) }
        doc.close()
        return file
    }

    private fun drawPair(canvas: Canvas, label: String, value: String, x: Float, y: Float,
                         w: Float, bold: TextPaint, normal: TextPaint, rule: Paint) {
        canvas.drawText(label + ":", x, y + 8f, bold)
        val vx = x + 96f
        canvas.drawText(value, vx, y + 8f, normal)
        canvas.drawLine(vx, y + 10f, x + w, y + 10f, rule)
    }

    private fun para(canvas: Canvas, text: String, x: Float, y: Float,
                     width: Int, paint: TextPaint): Float {
        val layout = StaticLayout.Builder
            .obtain(text, 0, text.length, paint, width)
            .setAlignment(Layout.Alignment.ALIGN_NORMAL)
            .setLineSpacing(1.5f, 1f)
            .setIncludePad(false)
            .build()
        canvas.save()
        canvas.translate(x, y)
        layout.draw(canvas)
        canvas.restore()
        return y + layout.height
    }
}
