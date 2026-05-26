package com.vkenterprises.vras.utils

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.util.Base64
import java.io.ByteArrayOutputStream

private const val IMG_MAX_DIM      = 1280
private const val IMG_JPEG_QUALITY = 80

/**
 * Decode -> downscale -> JPEG-compress an image URI, returning the result as
 * a base64 string ready to ship in a JSON payload.
 *
 * Two-pass decode: first just the bounds (no allocation) to compute the
 * sample size, then the actual bitmap at the target size — keeps peak
 * memory low even for huge camera photos. Max long side 1280 px, JPEG
 * quality 80. A 4 MB phone-camera photo lands at ~100-200 KB after this
 * runs, which lets the register / pfp-update calls fit in the OkHttp
 * timeout window even on slow networks.
 */
fun compressImageToBase64(context: Context, uri: Uri): String? {
    val cr = context.contentResolver

    val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
    cr.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, bounds) }
    if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null

    var sample = 1
    val longSide = maxOf(bounds.outWidth, bounds.outHeight)
    while (longSide / sample > IMG_MAX_DIM * 2) sample *= 2

    val opts = BitmapFactory.Options().apply { inSampleSize = sample }
    val bmp: Bitmap = cr.openInputStream(uri)?.use {
        BitmapFactory.decodeStream(it, null, opts)
    } ?: return null

    // Further scale to exactly max-side if still too large after subsampling.
    val scaled = if (maxOf(bmp.width, bmp.height) > IMG_MAX_DIM) {
        val ratio = IMG_MAX_DIM.toFloat() / maxOf(bmp.width, bmp.height)
        Bitmap.createScaledBitmap(bmp, (bmp.width * ratio).toInt(), (bmp.height * ratio).toInt(), true)
            .also { if (it !== bmp) bmp.recycle() }
    } else bmp

    val baos = ByteArrayOutputStream()
    scaled.compress(Bitmap.CompressFormat.JPEG, IMG_JPEG_QUALITY, baos)
    if (scaled !== bmp) scaled.recycle()

    return Base64.encodeToString(baos.toByteArray(), Base64.NO_WRAP)
}
