package com.vkenterprises.crmrs.utils

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.util.Base64
import java.io.ByteArrayOutputStream

private const val IMG_MAX_DIM      = 1280
private const val IMG_JPEG_QUALITY = 80

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
