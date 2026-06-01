package com.vkenterprises.vras.utils

import android.content.Context
import android.net.Uri
import com.google.mlkit.vision.common.InputImage
import com.google.mlkit.vision.text.TextRecognition
import com.google.mlkit.vision.text.latin.TextRecognizerOptions
import kotlin.coroutines.resume
import kotlinx.coroutines.suspendCancellableCoroutine

/**
 * On-device OCR for the Aadhaar front photo. Runs ML Kit Latin text recognition
 * (models bundled in the APK — fully offline, no network) and pulls the first
 * 12-digit Aadhaar-shaped number out of the recognised text.
 *
 * Aadhaar numbers are printed as three groups of four digits ("1234 5678 9012").
 * We tolerate optional spaces and validate with the official Verhoeff checksum
 * so a stray 12-digit string (a phone number split oddly, a VID, etc.) doesn't
 * get mistaken for the Aadhaar. Returns the 12 raw digits, or null if nothing
 * plausible was found — the screen then lets the agent type it manually.
 */
suspend fun extractAadhaarNumber(context: Context, uri: Uri): String? {
    val image = runCatching { InputImage.fromFilePath(context, uri) }.getOrNull() ?: return null
    val recognizer = TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS)
    val text: String = suspendCancellableCoroutine { cont ->
        recognizer.process(image)
            .addOnSuccessListener { cont.resume(it.text) }
            .addOnFailureListener { cont.resume("") }
            .addOnCanceledListener { cont.resume("") }
    }
    if (text.isBlank()) return null

    // Collapse whitespace so a number wrapped across lines still matches.
    val flat = text.replace("\n", " ")
    val regex = Regex("(?<!\\d)(\\d{4})\\s?(\\d{4})\\s?(\\d{4})(?!\\d)")
    for (m in regex.findAll(flat)) {
        val digits = m.groupValues.drop(1).joinToString("")
        if (digits.length == 12 && isVerhoeffValid(digits)) return digits
    }
    // Fallback: if no candidate passed the checksum, return the first 12-digit
    // group anyway — OCR can misread a digit; the agent confirms it on screen.
    return regex.find(flat)?.let { it.groupValues.drop(1).joinToString("") }
}

// Verhoeff checksum — the algorithm UIDAI uses for the Aadhaar number.
private val d = arrayOf(
    intArrayOf(0, 1, 2, 3, 4, 5, 6, 7, 8, 9),
    intArrayOf(1, 2, 3, 4, 0, 6, 7, 8, 9, 5),
    intArrayOf(2, 3, 4, 0, 1, 7, 8, 9, 5, 6),
    intArrayOf(3, 4, 0, 1, 2, 8, 9, 5, 6, 7),
    intArrayOf(4, 0, 1, 2, 3, 9, 5, 6, 7, 8),
    intArrayOf(5, 9, 8, 7, 6, 0, 4, 3, 2, 1),
    intArrayOf(6, 5, 9, 8, 7, 1, 0, 4, 3, 2),
    intArrayOf(7, 6, 5, 9, 8, 2, 1, 0, 4, 3),
    intArrayOf(8, 7, 6, 5, 9, 3, 2, 1, 0, 4),
    intArrayOf(9, 8, 7, 6, 5, 4, 3, 2, 1, 0)
)
private val p = arrayOf(
    intArrayOf(0, 1, 2, 3, 4, 5, 6, 7, 8, 9),
    intArrayOf(1, 5, 7, 6, 2, 8, 3, 0, 9, 4),
    intArrayOf(5, 8, 0, 3, 7, 9, 6, 1, 4, 2),
    intArrayOf(8, 9, 1, 6, 0, 4, 3, 5, 2, 7),
    intArrayOf(9, 4, 5, 3, 1, 2, 6, 8, 7, 0),
    intArrayOf(4, 2, 8, 6, 5, 7, 3, 9, 0, 1),
    intArrayOf(2, 7, 9, 3, 8, 0, 6, 4, 1, 5),
    intArrayOf(7, 0, 4, 6, 9, 1, 3, 2, 5, 8)
)

private fun isVerhoeffValid(num: String): Boolean {
    var c = 0
    val rev = num.reversed()
    for (i in rev.indices) {
        val digit = rev[i].digitToIntOrNull() ?: return false
        c = d[c][p[i % 8][digit]]
    }
    return c == 0
}
