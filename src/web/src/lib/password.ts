/** Letters and digits that cannot be mistaken for one another when read aloud or typed (no 0/O, 1/l/I). */
const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789';

/**
 * A random password for someone else to replace at their first sign-in: 20 characters from the
 * browser's cryptographic random source, well above the server's minimum of 12.
 */
export function generatePassword(length = 20): string {
  const bytes = new Uint8Array(length * 2);
  crypto.getRandomValues(bytes);
  let password = '';
  // Bytes at or above the largest multiple of the alphabet's size are skipped, so no letter is likelier.
  const limit = 256 - (256 % alphabet.length);
  for (const byte of bytes) {
    if (byte < limit) {
      password += alphabet[byte % alphabet.length];
    }
    if (password.length === length) {
      return password;
    }
  }
  return password + generatePassword(length - password.length);
}
