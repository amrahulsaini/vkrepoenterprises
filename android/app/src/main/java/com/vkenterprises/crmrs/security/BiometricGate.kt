package com.vkenterprises.crmrs.security

import androidx.biometric.BiometricManager
import androidx.biometric.BiometricPrompt
import androidx.core.content.ContextCompat
import androidx.fragment.app.FragmentActivity
import java.security.Signature
import kotlin.coroutines.resume
import kotlinx.coroutines.suspendCancellableCoroutine

object BiometricGate {

    private const val STRONG = BiometricManager.Authenticators.BIOMETRIC_STRONG

    sealed class Ready {
        object Yes : Ready()
        object NoHardware : Ready()
        object NotEnrolled : Ready()
        data class Unavailable(val reason: String) : Ready()
    }

    fun ready(activity: FragmentActivity): Ready =
        when (BiometricManager.from(activity).canAuthenticate(STRONG)) {
            BiometricManager.BIOMETRIC_SUCCESS -> Ready.Yes
            BiometricManager.BIOMETRIC_ERROR_NO_HARDWARE,
            BiometricManager.BIOMETRIC_ERROR_HW_UNAVAILABLE -> Ready.NoHardware
            BiometricManager.BIOMETRIC_ERROR_NONE_ENROLLED -> Ready.NotEnrolled
            else -> Ready.Unavailable("Fingerprint is not available on this phone.")
        }

    data class Result(val ok: Boolean, val signature: Signature?, val error: String?)

    /**
     * Shows the system fingerprint prompt. The Signature is only usable if the
     * fingerprint succeeded, because the key demands authentication per use.
     */
    suspend fun authenticate(
        activity: FragmentActivity,
        title: String,
        subtitle: String,
        signature: Signature
    ): Result = suspendCancellableCoroutine { cont ->
        val prompt = BiometricPrompt(
            activity,
            ContextCompat.getMainExecutor(activity),
            object : BiometricPrompt.AuthenticationCallback() {
                override fun onAuthenticationSucceeded(result: BiometricPrompt.AuthenticationResult) {
                    if (cont.isActive) cont.resume(Result(true, result.cryptoObject?.signature, null))
                }

                override fun onAuthenticationError(code: Int, msg: CharSequence) {
                    if (cont.isActive) cont.resume(Result(false, null, msg.toString()))
                }

                override fun onAuthenticationFailed() = Unit
            }
        )

        val info = BiometricPrompt.PromptInfo.Builder()
            .setTitle(title)
            .setSubtitle(subtitle)
            .setNegativeButtonText("Cancel")
            .setAllowedAuthenticators(STRONG)
            .setConfirmationRequired(false)
            .build()

        prompt.authenticate(info, BiometricPrompt.CryptoObject(signature))
        cont.invokeOnCancellation { prompt.cancelAuthentication() }
    }
}
