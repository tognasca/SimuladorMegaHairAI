using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimuladorMegaHair.Domain.Enums
{
    public enum HairEditMode
    {
        /// <summary>
        /// Cliente quer mega hair ou cabelo mais longo que o atual.
        /// A máscara cobre o cabelo atual + a área para onde o cabelo vai crescer.
        /// </summary>
        Extend,

        /// <summary>
        /// Cliente tem cabelo longo e quer simular corte mais curto.
        /// A máscara cobre 100% do cabelo visível para a IA "apagar" o longo e reconstruir o fundo/ombros.
        /// </summary>
        Shorten,

        /// <summary>
        /// Mantém comprimento/estilo similar, focando na mudança de cor.
        /// A máscara cobre 100% do cabelo visível existente.
        /// </summary>
        Recolor
    }
}
