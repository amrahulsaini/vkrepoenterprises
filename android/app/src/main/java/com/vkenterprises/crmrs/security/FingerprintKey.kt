package com.vkenterprises.crmrs.security

import android.os.Build
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.Signature

object FingerprintKey {

    private const val ALIAS = "crmrs_fingerprint_v1"
    private const val PROVIDER = "AndroidKeyStore"

    private fun keyStore(): KeyStore =
        KeyStore.getInstance(PROVIDER).apply { load(null) }

    fun exists(): Boolean = runCatching { keyStore().containsAlias(ALIAS) }.getOrDefault(false)

    fun delete() {
        runCatching { keyStore().deleteEntry(ALIAS) }
    }

    /**
     * Generates the keypair inside the phone's secure hardware. The private key
     * can never leave it, and every use of it demands a fresh fingerprint.
     */
    fun create(): String {
        delete()

        val gen = KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_EC, PROVIDER)
        val spec = KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_SIGN)
            .setDigests(KeyProperties.DIGEST_SHA256)
            .setAlgorithmParameterSpec(java.security.spec.ECGenParameterSpec("secp256r1"))
            .setUserAuthenticationRequired(true)
            .apply {
                // Destroys the key the moment any new fingerprint is added to the
                // phone, so someone who borrows an unlocked handset cannot enrol
                // their own finger and then sign in as its owner.
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
                    setInvalidatedByBiometricEnrollment(true)
                }
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                    setIsStrongBoxBacked(true)
                }
            }

        val pair = try {
            gen.initialize(spec.build()); gen.generateKeyPair()
        } catch (e: Exception) {
            // StrongBox is not on every device; fall back to the TEE.
            val fallback = KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_SIGN)
                .setDigests(KeyProperties.DIGEST_SHA256)
                .setAlgorithmParameterSpec(java.security.spec.ECGenParameterSpec("secp256r1"))
                .setUserAuthenticationRequired(true)
                .apply {
                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
                        setInvalidatedByBiometricEnrollment(true)
                    }
                }
                .build()
            gen.initialize(fallback); gen.generateKeyPair()
        }

        return Base64.encodeToString(pair.public.encoded, Base64.NO_WRAP)
    }

    fun publicKeyBase64(): String? = runCatching {
        val cert = keyStore().getCertificate(ALIAS) ?: return null
        Base64.encodeToString(cert.publicKey.encoded, Base64.NO_WRAP)
    }.getOrNull()

    /**
     * Signature object primed with the private key. BiometricPrompt unlocks it;
     * without a successful fingerprint it throws rather than signing.
     */
    fun signatureForPrompt(): Signature {
        val entry = keyStore().getEntry(ALIAS, null) as KeyStore.PrivateKeyEntry
        return Signature.getInstance("SHA256withECDSA").apply { initSign(entry.privateKey) }
    }

    fun finish(signature: Signature, message: String): String {
        signature.update(message.toByteArray(Charsets.UTF_8))
        return Base64.encodeToString(signature.sign(), Base64.NO_WRAP)
    }
}
