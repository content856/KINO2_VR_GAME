package com.kinovr.securestorage;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.security.KeyStore;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** Tokens never leave app-private storage unencrypted. No user-auth prompt is required. */
public final class KinoSecureStorage {
    private static final String ALIAS = "kinovr.api.tokens.v1";
    private static final String PREFS = "kinovr_api_encrypted";
    private KinoSecureStorage() { }

    private static SecretKey key() throws Exception {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore");
        store.load(null);
        if (store.containsAlias(ALIAS)) return (SecretKey) store.getKey(ALIAS, null);
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(ALIAS,
                KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setRandomizedEncryptionRequired(true).build());
        return generator.generateKey();
    }

    private static SharedPreferences prefs(Context context) {
        return context.getApplicationContext().getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    public static synchronized String read(Context context, String scope) throws Exception {
        String saved = prefs(context).getString(scope, null);
        if (saved == null) return null;
        String[] parts = saved.split(":", -1);
        if (parts.length != 3 || !"1".equals(parts[0])) throw new IllegalStateException("Unknown storage format");
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, Base64.decode(parts[1], Base64.NO_WRAP)));
        cipher.updateAAD(scope.getBytes("UTF-8"));
        return new String(cipher.doFinal(Base64.decode(parts[2], Base64.NO_WRAP)), "UTF-8");
    }

    public static synchronized void write(Context context, String scope, String json) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, key());
        cipher.updateAAD(scope.getBytes("UTF-8"));
        String saved = "1:" + Base64.encodeToString(cipher.getIV(), Base64.NO_WRAP) + ":" +
                Base64.encodeToString(cipher.doFinal(json.getBytes("UTF-8")), Base64.NO_WRAP);
        // commit, not apply: the refresh marker/new token must be durable before network use.
        if (!prefs(context).edit().putString(scope, saved).commit()) throw new IllegalStateException("Storage commit failed");
    }

    public static synchronized void clear(Context context, String scope) {
        if (!prefs(context).edit().remove(scope).commit()) throw new IllegalStateException("Storage clear failed");
    }
}
